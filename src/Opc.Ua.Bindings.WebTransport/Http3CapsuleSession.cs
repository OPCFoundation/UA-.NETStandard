// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Validates the response field section for a capsule-based WebTransport CONNECT.
    /// </summary>
    internal static class Http3ExtendedConnectResponseParser
    {
        /// <summary>
        /// Parses the response status and Capsule-Protocol indication.
        /// </summary>
        internal static bool TryParse(
            ReadOnlySpan<QpackHeaderField> fields,
            out int statusCode,
            out bool capsuleProtocolEnabled)
        {
            string? status = null;
            bool regularHeadersStarted = false;
            bool capsuleProtocolSeen = false;
            capsuleProtocolEnabled = false;

            foreach (QpackHeaderField field in fields)
            {
                if (field.Name.Length > 0 && field.Name[0] == ':')
                {
                    if (regularHeadersStarted || field.Name != ":status" || status != null)
                    {
                        statusCode = 0;
                        return false;
                    }

                    status = field.Value;
                    continue;
                }

                regularHeadersStarted = true;
                if (field.Name == "capsule-protocol")
                {
                    if (capsuleProtocolSeen)
                    {
                        statusCode = 0;
                        return false;
                    }

                    capsuleProtocolSeen = true;
                    capsuleProtocolEnabled = field.Value == "?1";
                }
            }

            if (status is not { Length: 3 } ||
                status[0] is < '1' or > '5' ||
                status[1] is < '0' or > '9' ||
                status[2] is < '0' or > '9' ||
                !int.TryParse(status, NumberStyles.None, CultureInfo.InvariantCulture, out statusCode))
            {
                statusCode = 0;
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Reads and writes WebTransport capsules carried in HTTP/3 DATA frames.
    /// </summary>
    internal sealed class Http3CapsuleSession : IAsyncDisposable
    {
        /// <summary>
        /// Creates a capsule session over an accepted HTTP/3 CONNECT stream.
        /// </summary>
        private Http3CapsuleSession(
            Stream stream,
            int maximumFramePayloadBytes,
            int maximumCapsulePayloadBytes,
            IAsyncDisposable? owner = null)
        {
            m_stream = stream;
            m_maximumFramePayloadBytes = maximumFramePayloadBytes;
            m_maximumCapsulePayloadBytes = maximumCapsulePayloadBytes;
            m_dataPayloadStream = new Http3DataFramePayloadStream(stream, maximumFramePayloadBytes);
            m_owner = owner;
        }

        /// <summary>
        /// Writes the client's Extended CONNECT request as the first HTTP/3 request-stream frame.
        /// </summary>
        internal static async ValueTask WriteConnectRequestAsync(
            Stream stream,
            string authority,
            string path,
            string? origin,
            int maximumHeaderBytes,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfNegative(maximumHeaderBytes);
            if (!QpackConnectRequestEncoder.TryEncode(
                authority, path, origin, maximumHeaderBytes, out byte[] fieldSection))
            {
                throw new InvalidDataException("The WebTransport CONNECT field section exceeds its configured limit.");
            }

            await Http3StreamCodec.WriteFrameAsync(stream, HeadersFrameType, fieldSection, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Accepts a successful CONNECT response that explicitly enables Capsule Protocol.
        /// </summary>
        internal static async ValueTask<Http3CapsuleSession> AcceptConnectResponseAsync(
            Stream stream,
            int maximumHeaderBytes,
            int maximumFramePayloadBytes,
            int maximumCapsulePayloadBytes,
            CancellationToken cancellationToken,
            IAsyncDisposable? owner = null)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfNegative(maximumHeaderBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFramePayloadBytes);
            ArgumentOutOfRangeException.ThrowIfNegative(maximumCapsulePayloadBytes);

            for (int interimResponses = 0; interimResponses <= MaximumInterimResponses; interimResponses++)
            {
                Http3Frame? frame = await Http3StreamCodec.ReadFrameAsync(
                    stream, maximumHeaderBytes, cancellationToken).ConfigureAwait(false);
                if (frame is not { Type: HeadersFrameType })
                {
                    throw new InvalidDataException("The CONNECT response must begin with a HEADERS frame.");
                }

                QpackDecodeStatus decodeStatus = QpackFieldSectionDecoder.TryDecode(
                    frame.Value.Payload, maximumHeaderBytes, maximumHeaderBytes, MaximumResponseFields, out
                        QpackHeaderField[] fields);
                if (decodeStatus != QpackDecodeStatus.Success ||
                    !Http3ExtendedConnectResponseParser.TryParse(
                        fields, out int statusCode, out bool capsuleProtocol))
                {
                    throw new InvalidDataException("The CONNECT response field section is invalid.");
                }
                if (statusCode is >= 100 and < 200 and not 101)
                {
                    continue;
                }
                if (statusCode is < 200 or >= 300 || !capsuleProtocol)
                {
                    throw new InvalidDataException(
                        "The CONNECT response did not establish the capsule-based WebTransport session.");
                }

                return new Http3CapsuleSession(
                    stream, maximumFramePayloadBytes, maximumCapsulePayloadBytes, owner);
            }

            throw new InvalidDataException("The CONNECT response exceeded the interim response limit.");
        }

        /// <summary>
        /// Reads and validates the server-side WebTransport Extended CONNECT request headers.
        /// </summary>
        internal static async ValueTask<Http3ExtendedConnectRequest> ReadConnectRequestAsync(
            Stream stream,
            int maximumHeaderBytes,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumHeaderBytes);
            Http3Frame? frame = await Http3StreamCodec.ReadFrameAsync(
                stream, maximumHeaderBytes, cancellationToken).ConfigureAwait(false);
            if (frame is not { Type: HeadersFrameType })
            {
                throw new InvalidDataException("The WebTransport request must begin with a HEADERS frame.");
            }

            QpackDecodeStatus decodeStatus = QpackFieldSectionDecoder.TryDecode(
                frame.Value.Payload, maximumHeaderBytes, maximumHeaderBytes, MaximumResponseFields, out
                    QpackHeaderField[] fields);
            if (decodeStatus != QpackDecodeStatus.Success ||
                Http3ExtendedConnectRequestParser.TryParse(fields, out Http3ExtendedConnectRequest request) !=
                    Http3ExtendedConnectRequestStatus.Success)
            {
                throw new InvalidDataException("The WebTransport CONNECT request field section is invalid.");
            }
            return request;
        }

        /// <summary>
        /// Writes a successful CONNECT response and starts capsule framing on the request stream.
        /// </summary>
        internal static async ValueTask<Http3CapsuleSession> WriteConnectResponseAsync(
            Stream stream,
            int maximumHeaderBytes,
            int maximumFramePayloadBytes,
            int maximumCapsulePayloadBytes,
            CancellationToken cancellationToken,
            IAsyncDisposable? owner = null)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumHeaderBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFramePayloadBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCapsulePayloadBytes);
            if (!QpackConnectRequestEncoder.TryEncodeCapsuleConnectResponse(
                maximumHeaderBytes, out byte[] fieldSection))
            {
                throw new InvalidDataException("The WebTransport CONNECT response exceeds its configured limit.");
            }

            await Http3StreamCodec.WriteFrameAsync(
                stream, HeadersFrameType, fieldSection, cancellationToken).ConfigureAwait(false);
            return new Http3CapsuleSession(
                stream, maximumFramePayloadBytes, maximumCapsulePayloadBytes, owner);
        }

        /// <summary>
        /// Rejects an unauthorized or unsupported request without signaling Capsule Protocol.
        /// </summary>
        internal static async ValueTask WriteConnectFailureAsync(
            Stream stream,
            int statusCode,
            int maximumHeaderBytes,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (statusCode is < 400 or > 599)
            {
                throw new ArgumentOutOfRangeException(nameof(statusCode));
            }
            if (!QpackConnectRequestEncoder.TryEncodeCapsuleConnectResponse(
                statusCode, maximumHeaderBytes, out byte[] fieldSection))
            {
                throw new InvalidDataException("The WebTransport failure response exceeds its configured limit.");
            }

            await Http3StreamCodec.WriteFrameAsync(
                stream, HeadersFrameType, fieldSection, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads one capsule from the concatenated payloads of subsequent HTTP/3 DATA frames.
        /// </summary>
        internal async ValueTask<WebTransportCapsule?> ReadCapsuleAsync(CancellationToken cancellationToken)
        {
            await m_readGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await Http3StreamCodec.ReadCapsuleAsync(
                    m_dataPayloadStream, m_maximumCapsulePayloadBytes, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_readGate.Release();
            }
        }

        /// <summary>
        /// Writes one capsule, splitting its encoded bytes across bounded HTTP/3 DATA frames as needed.
        /// </summary>
        internal async ValueTask WriteCapsuleAsync(
            ulong type,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            if (payload.Length > m_maximumCapsulePayloadBytes)
            {
                throw new InvalidDataException("The WebTransport capsule exceeds its configured payload bound.");
            }

            int typeLength = Http3VarInt.GetEncodedLength(type);
            int payloadLengthLength = Http3VarInt.GetEncodedLength((ulong)payload.Length);
            if (typeLength == 0 || payloadLengthLength == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(type));
            }

            byte[] capsule = new byte[checked(typeLength + payloadLengthLength + payload.Length)];
            if (!Http3VarInt.TryWrite(capsule, type, out int typeBytes) ||
                !Http3VarInt.TryWrite(
                    capsule.AsSpan(typeBytes), (ulong)payload.Length, out int lengthBytes))
            {
                throw new InvalidOperationException("The WebTransport capsule header could not be encoded.");
            }
            payload.Span.CopyTo(capsule.AsSpan(typeBytes + lengthBytes));

            await m_writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                for (int offset = 0; offset < capsule.Length;)
                {
                    int count = Math.Min(m_maximumFramePayloadBytes, capsule.Length - offset);
                    await Http3StreamCodec.WriteFrameAsync(
                        m_stream, DataFrameType, capsule.AsMemory(offset, count), cancellationToken)
                        .ConfigureAwait(false);
                    offset += count;
                }
            }
            finally
            {
                m_writeGate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            try
            {
                if (m_owner != null)
                {
                    await m_owner.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await m_stream.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await m_dataPayloadStream.DisposeAsync().ConfigureAwait(false);
                m_readGate.Dispose();
                m_writeGate.Dispose();
            }
        }

        private const ulong DataFrameType = 0x0;
        private const ulong HeadersFrameType = 0x1;
        private const int MaximumResponseFields = 64;
        private const int MaximumInterimResponses = 8;

        private readonly Stream m_stream;
        private readonly int m_maximumFramePayloadBytes;
        private readonly int m_maximumCapsulePayloadBytes;
        private readonly Http3DataFramePayloadStream m_dataPayloadStream;
        private readonly IAsyncDisposable? m_owner;
        private readonly SemaphoreSlim m_readGate = new(1, 1);
        private readonly SemaphoreSlim m_writeGate = new(1, 1);
        private int m_disposed;

        private sealed class Http3DataFramePayloadStream(Stream stream, int maximumFramePayloadBytes) : Stream
        {
            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException("HTTP/3 DATA payload reads are asynchronous.");
            }

            public override ValueTask<int> ReadAsync(
                Memory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                return ReadPayloadAsync(buffer, cancellationToken);
            }

            public override void Flush()
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            private async ValueTask<int> ReadPayloadAsync(
                Memory<byte> destination,
                CancellationToken cancellationToken)
            {
                if (destination.IsEmpty)
                {
                    return 0;
                }

                while (m_remaining == 0)
                {
                    Http3Frame? frame = await Http3StreamCodec.ReadFrameAsync(
                        stream, maximumFramePayloadBytes, cancellationToken).ConfigureAwait(false);
                    if (frame == null)
                    {
                        return 0;
                    }
                    if (frame.Value.Type == DataFrameType)
                    {
                        m_payload = frame.Value.Payload;
                        m_position = m_payload.Start;
                        m_remaining = m_payload.Length;
                        continue;
                    }
                    if (IsKnownFrameType(frame.Value.Type))
                    {
                        throw new InvalidDataException(
                            "An HTTP/3 frame that is not valid in the CONNECT body was received.");
                    }
                }

                int count = (int)Math.Min(destination.Length, m_remaining);
                m_payload.Slice(m_position, count).CopyTo(destination.Span);
                m_position = m_payload.GetPosition(count, m_position);
                m_remaining -= count;
                return count;
            }

            private static bool IsKnownFrameType(ulong frameType)
            {
                return frameType is 0x1 or 0x2 or 0x3 or 0x4 or 0x5 or 0x6 or 0x7 or 0x8 or 0x9 or 0xd;
            }

            private ReadOnlySequence<byte> m_payload;
            private SequencePosition m_position;
            private long m_remaining;
        }
    }
}
