// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Reads and writes bounded HTTP/3 frames and WebTransport capsules on reliable streams.
    /// </summary>
    internal static class Http3StreamCodec
    {
        /// <summary>
        /// Reads one complete frame, returning null only when EOF occurs before any frame-header byte.
        /// </summary>
        internal static async ValueTask<Http3Frame?> ReadFrameAsync(
            Stream stream,
            int maximumPayloadBytes,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfNegative(maximumPayloadBytes);

            byte[] header = new byte[16];
            int headerLength = await ReadVarIntAsync(stream, header, 0, allowCleanEof: true, cancellationToken)
                .ConfigureAwait(false);
            if (headerLength == 0)
            {
                return null;
            }

            int lengthStart = headerLength;
            int lengthBytes = await ReadVarIntAsync(
                stream, header, lengthStart, allowCleanEof: false, cancellationToken).ConfigureAwait(false);
            headerLength += lengthBytes;
            SequenceReader<byte> reader = new(new ReadOnlySequence<byte>(header, 0, headerLength));
            if (!Http3VarInt.TryRead(ref reader, out ulong type) ||
                !Http3VarInt.TryRead(ref reader, out ulong payloadLength))
            {
                throw new InvalidDataException("The HTTP/3 frame header was incomplete.");
            }
            if (payloadLength > (ulong)maximumPayloadBytes)
            {
                throw new InvalidDataException("The HTTP/3 frame exceeds its configured payload bound.");
            }

            byte[] payload = new byte[(int)payloadLength];
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            return new Http3Frame(type, new ReadOnlySequence<byte>(payload));
        }

        /// <summary>
        /// Reads one complete capsule, returning null only when EOF occurs before any capsule-header byte.
        /// </summary>
        internal static async ValueTask<WebTransportCapsule?> ReadCapsuleAsync(
            Stream stream,
            int maximumPayloadBytes,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfNegative(maximumPayloadBytes);

            byte[] header = new byte[16];
            int headerLength = await ReadVarIntAsync(stream, header, 0, allowCleanEof: true, cancellationToken)
                .ConfigureAwait(false);
            if (headerLength == 0)
            {
                return null;
            }

            int lengthBytes = await ReadVarIntAsync(
                stream, header, headerLength, allowCleanEof: false, cancellationToken).ConfigureAwait(false);
            headerLength += lengthBytes;
            SequenceReader<byte> reader = new(new ReadOnlySequence<byte>(header, 0, headerLength));
            if (!Http3VarInt.TryRead(ref reader, out ulong type) ||
                !Http3VarInt.TryRead(ref reader, out ulong payloadLength))
            {
                throw new InvalidDataException("The WebTransport capsule header was incomplete.");
            }
            if (payloadLength > (ulong)maximumPayloadBytes)
            {
                throw new InvalidDataException("The WebTransport capsule exceeds its configured payload bound.");
            }

            byte[] payload = new byte[(int)payloadLength];
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            return new WebTransportCapsule(type, new ReadOnlySequence<byte>(payload));
        }

        /// <summary>
        /// Writes one complete HTTP/3 frame using shortest-form QUIC variable-length integers.
        /// </summary>
        internal static async ValueTask WriteFrameAsync(
            Stream stream,
            ulong type,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            await WriteRecordAsync(stream, type, payload, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes one complete WebTransport capsule using shortest-form QUIC variable-length integers.
        /// </summary>
        internal static async ValueTask WriteCapsuleAsync(
            Stream stream,
            ulong type,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            await WriteRecordAsync(stream, type, payload, cancellationToken).ConfigureAwait(false);
        }

        private static async ValueTask WriteRecordAsync(
            Stream stream,
            ulong type,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            int typeLength = Http3VarInt.GetEncodedLength(type);
            int payloadLengthLength = Http3VarInt.GetEncodedLength((ulong)payload.Length);
            if (typeLength == 0 || payloadLengthLength == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(type));
            }

            byte[] header = new byte[typeLength + payloadLengthLength];
            if (!Http3VarInt.TryWrite(header, type, out int written) ||
                !Http3VarInt.TryWrite(header.AsSpan(written), (ulong)payload.Length, out int lengthWritten) ||
                written + lengthWritten != header.Length)
            {
                throw new InvalidOperationException("The HTTP/3 record header could not be encoded.");
            }

            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            if (!payload.IsEmpty)
            {
                await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async ValueTask<int> ReadVarIntAsync(
            Stream stream,
            byte[] header,
            int offset,
            bool allowCleanEof,
            CancellationToken cancellationToken)
        {
            int firstByteRead = await stream.ReadAsync(header.AsMemory(offset, 1), cancellationToken)
                .ConfigureAwait(false);
            if (firstByteRead == 0)
            {
                if (allowCleanEof)
                {
                    return 0;
                }
                throw new EndOfStreamException("The HTTP/3 record header ended before its length.");
            }

            int encodedLength = 1 << (header[offset] >> 6);
            if (encodedLength > 1)
            {
                await stream.ReadExactlyAsync(
                    header.AsMemory(offset + 1, encodedLength - 1), cancellationToken).ConfigureAwait(false);
            }
            return encodedLength;
        }
    }
}
