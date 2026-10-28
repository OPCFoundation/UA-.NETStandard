// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers;
using System.Buffers.Binary;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Encodes and decodes the variable-length integer format used by QUIC and HTTP/3.
    /// </summary>
    internal static class Http3VarInt
    {
        /// <summary>
        /// Gets the encoded length of a value, or zero when it is not representable.
        /// </summary>
        internal static int GetEncodedLength(ulong value)
        {
            return value switch
            {
                <= 63 => 1,
                <= 16383 => 2,
                <= 1073741823 => 4,
                <= MaximumValue => 8,
                _ => 0
            };
        }

        /// <summary>
        /// Attempts to read one variable-length integer without consuming partial input.
        /// </summary>
        internal static bool TryRead(ref SequenceReader<byte> reader, out ulong value)
        {
            SequenceReader<byte> candidate = reader;
            if (!candidate.TryRead(out byte first))
            {
                value = 0;
                return false;
            }

            int encodedLength = 1 << (first >> 6);
            Span<byte> encoded = stackalloc byte[8];
            encoded[0] = (byte)(first & 0x3f);
            if (!candidate.TryCopyTo(encoded[1..encodedLength]))
            {
                value = 0;
                return false;
            }
            candidate.Advance(encodedLength - 1);

            value = encodedLength switch
            {
                1 => encoded[0],
                2 => (ulong)(BinaryPrimitives.ReadUInt16BigEndian(encoded) & 0x3fff),
                4 => BinaryPrimitives.ReadUInt32BigEndian(encoded) & 0x3fffffff,
                8 => BinaryPrimitives.ReadUInt64BigEndian(encoded) & MaximumValue,
                _ => throw new InvalidOperationException("Invalid QUIC variable-length integer size.")
            };
            reader = candidate;
            return true;
        }

        /// <summary>
        /// Attempts to write a variable-length integer in its shortest valid encoding.
        /// </summary>
        internal static bool TryWrite(Span<byte> destination, ulong value, out int bytesWritten)
        {
            int encodedLength = GetEncodedLength(value);
            if (encodedLength == 0 || destination.Length < encodedLength)
            {
                bytesWritten = 0;
                return false;
            }

            switch (encodedLength)
            {
                case 1:
                    destination[0] = (byte)value;
                    break;
                case 2:
                    BinaryPrimitives.WriteUInt16BigEndian(destination, (ushort)(value | 0x4000));
                    break;
                case 4:
                    BinaryPrimitives.WriteUInt32BigEndian(destination, (uint)(value | 0x80000000));
                    break;
                case 8:
                    BinaryPrimitives.WriteUInt64BigEndian(destination, value | 0xc000000000000000);
                    break;
            }

            bytesWritten = encodedLength;
            return true;
        }

        /// <summary>
        /// Gets the largest value representable by a QUIC variable-length integer.
        /// </summary>
        internal const ulong MaximumValue = (1UL << 62) - 1;
    }

    /// <summary>
    /// Represents the type and bounded payload length in an HTTP/3 frame header.
    /// </summary>
    internal readonly record struct Http3FrameHeader(ulong Type, ulong Length)
    {
        /// <summary>
        /// Attempts to read a complete frame header, leaving partial or oversized input unconsumed.
        /// </summary>
        internal static Http3FrameHeaderReadStatus TryRead(
            ref SequenceReader<byte> reader,
            ulong maximumPayloadLength,
            out Http3FrameHeader header)
        {
            if (maximumPayloadLength > Http3VarInt.MaximumValue)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumPayloadLength));
            }

            SequenceReader<byte> candidate = reader;
            if (!Http3VarInt.TryRead(ref candidate, out ulong type) ||
                !Http3VarInt.TryRead(ref candidate, out ulong length))
            {
                header = default;
                return Http3FrameHeaderReadStatus.NeedMoreData;
            }

            if (length > maximumPayloadLength)
            {
                header = default;
                return Http3FrameHeaderReadStatus.PayloadTooLarge;
            }

            reader = candidate;
            header = new Http3FrameHeader(type, length);
            return Http3FrameHeaderReadStatus.Success;
        }
    }

    /// <summary>
    /// Describes the result of reading an HTTP/3 frame header.
    /// </summary>
    internal enum Http3FrameHeaderReadStatus
    {
        /// <summary>
        /// The input does not contain a complete header.
        /// </summary>
        NeedMoreData,

        /// <summary>
        /// The declared payload exceeds the configured bound.
        /// </summary>
        PayloadTooLarge,

        /// <summary>
        /// A complete, bounded header was read.
        /// </summary>
        Success
    }

    /// <summary>
    /// Represents one complete HTTP/3 frame and its borrowed payload bytes.
    /// </summary>
    internal readonly record struct Http3Frame(ulong Type, ReadOnlySequence<byte> Payload)
    {
        /// <summary>
        /// Gets whether the frame type is reserved for extensibility by HTTP/3.
        /// </summary>
        internal bool IsReservedType => Type >= 0x21 && (Type - 0x21) % 0x1f == 0;

        /// <summary>
        /// Attempts to read one complete bounded frame without consuming partial input.
        /// </summary>
        internal static Http3FrameReadStatus TryRead(
            ref SequenceReader<byte> reader,
            ulong maximumPayloadLength,
            out Http3Frame frame)
        {
            SequenceReader<byte> candidate = reader;
            Http3FrameHeaderReadStatus headerStatus =
                Http3FrameHeader.TryRead(ref candidate, maximumPayloadLength, out Http3FrameHeader header);
            if (headerStatus == Http3FrameHeaderReadStatus.NeedMoreData)
            {
                frame = default;
                return Http3FrameReadStatus.NeedMoreData;
            }
            if (headerStatus == Http3FrameHeaderReadStatus.PayloadTooLarge)
            {
                frame = default;
                return Http3FrameReadStatus.PayloadTooLarge;
            }

            long payloadLength = checked((long)header.Length);
            if (candidate.Remaining < payloadLength)
            {
                frame = default;
                return Http3FrameReadStatus.NeedMoreData;
            }

            ReadOnlySequence<byte> payload = candidate.Sequence.Slice(candidate.Position, payloadLength);
            candidate.Advance(payloadLength);
            reader = candidate;
            frame = new Http3Frame(header.Type, payload);
            return Http3FrameReadStatus.Success;
        }
    }

    /// <summary>
    /// Describes the result of reading one HTTP/3 frame.
    /// </summary>
    internal enum Http3FrameReadStatus
    {
        /// <summary>
        /// The input does not contain a complete frame.
        /// </summary>
        NeedMoreData,

        /// <summary>
        /// The declared payload exceeds the configured bound.
        /// </summary>
        PayloadTooLarge,

        /// <summary>
        /// A complete bounded frame was read.
        /// </summary>
        Success
    }

    /// <summary>
    /// Represents one complete capsule and its borrowed payload bytes.
    /// </summary>
    internal readonly record struct WebTransportCapsule(ulong Type, ReadOnlySequence<byte> Payload)
    {
        /// <summary>
        /// Attempts to read one complete, bounded capsule without consuming partial input.
        /// </summary>
        internal static WebTransportCapsuleReadStatus TryRead(
            ref SequenceReader<byte> reader,
            ulong maximumPayloadLength,
            out WebTransportCapsule capsule)
        {
            SequenceReader<byte> candidate = reader;
            Http3FrameHeaderReadStatus headerStatus =
                Http3FrameHeader.TryRead(ref candidate, maximumPayloadLength, out Http3FrameHeader header);
            if (headerStatus == Http3FrameHeaderReadStatus.NeedMoreData)
            {
                capsule = default;
                return WebTransportCapsuleReadStatus.NeedMoreData;
            }
            if (headerStatus == Http3FrameHeaderReadStatus.PayloadTooLarge)
            {
                capsule = default;
                return WebTransportCapsuleReadStatus.PayloadTooLarge;
            }

            long payloadLength = checked((long)header.Length);
            if (candidate.Remaining < payloadLength)
            {
                capsule = default;
                return WebTransportCapsuleReadStatus.NeedMoreData;
            }

            ReadOnlySequence<byte> payload = candidate.Sequence.Slice(candidate.Position, payloadLength);
            candidate.Advance(payloadLength);
            reader = candidate;
            capsule = new WebTransportCapsule(header.Type, payload);
            return WebTransportCapsuleReadStatus.Success;
        }
    }

    /// <summary>
    /// Describes the result of reading a WebTransport capsule.
    /// </summary>
    internal enum WebTransportCapsuleReadStatus
    {
        /// <summary>
        /// The input does not contain a complete capsule.
        /// </summary>
        NeedMoreData,

        /// <summary>
        /// The declared payload exceeds the configured bound.
        /// </summary>
        PayloadTooLarge,

        /// <summary>
        /// A complete, bounded capsule was read.
        /// </summary>
        Success
    }
}
