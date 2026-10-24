/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Opc.Ua.DataChannels.Wire
{
    /// <summary>
    /// Holds a decoded application-payload NetworkMessage header and its borrowed payload bytes.
    /// </summary>
    internal readonly struct UadpApplicationPayload
    {
        /// <summary>
        /// Creates a decoded application payload.
        /// </summary>
        internal UadpApplicationPayload(Variant publisherId, ByteString payload)
        {
            PublisherId = publisherId;
            Payload = payload;
        }

        /// <summary>
        /// Gets the typed UADP PublisherId.
        /// </summary>
        internal Variant PublisherId { get; }

        /// <summary>
        /// Gets the payload following the minimal UADP application header.
        /// </summary>
        internal ByteString Payload { get; }
    }

    /// <summary>
    /// Holds the visible fields from the beginning of a UADP NetworkMessage.
    /// </summary>
    internal readonly struct UadpNetworkMessageHeader
    {
        /// <summary>
        /// Creates a decoded UADP NetworkMessage header.
        /// </summary>
        internal UadpNetworkMessageHeader(
            byte networkMessageType,
            Variant publisherId,
            ushort? writerGroupId = null,
            ushort[]? dataSetWriterIds = null)
        {
            NetworkMessageType = networkMessageType;
            PublisherId = publisherId;
            WriterGroupId = writerGroupId;
            DataSetWriterIds = dataSetWriterIds ?? [];
        }

        /// <summary>
        /// Gets the NetworkMessageType value from ExtendedFlags2, or zero when omitted.
        /// </summary>
        internal byte NetworkMessageType { get; }

        /// <summary>
        /// Gets the typed UADP PublisherId, or null when it is omitted.
        /// </summary>
        internal Variant PublisherId { get; }

        /// <summary>
        /// Gets the visible WriterGroupId, or null when it is absent or hidden by security.
        /// </summary>
        internal ushort? WriterGroupId { get; }

        /// <summary>
        /// Gets the visible DataSetWriterIds in the payload header.
        /// </summary>
        internal ushort[] DataSetWriterIds { get; }
    }

    /// <summary>
    /// Encodes and decodes the amended minimal UADP application-payload header without PubSub dependencies.
    /// </summary>
    internal static class UadpApplicationPayloadHeaderCodec
    {
        /// <summary>
        /// Encodes one complete minimal application-payload NetworkMessage.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="FlowCodecException"></exception>
        internal static ByteString Encode(Variant publisherId, ByteString payload, int maximumMessageBytes)
        {
            if (maximumMessageBytes < FixedHeaderSize)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumMessageBytes));
            }
            if (payload.IsNull)
            {
                throw new FlowCodecException(FlowCodecError.WrongType);
            }

            PublisherValue value = GetPublisherValue(publisherId);
            int encodedPublisherLength = GetPublisherLength(value);
            long messageLength = (long)FixedHeaderSize + encodedPublisherLength + payload.Length;
            if (messageLength > maximumMessageBytes)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }

            var encoded = new byte[(int)messageLength];
            encoded[0] = UadpFlags;
            encoded[1] = (byte)(ExtendedFlags1Enabled | value.Type);
            encoded[2] = ApplicationNetworkMessageTypeBits;
            WritePublisherValue(value, encoded, FixedHeaderSize);
            payload.Span.CopyTo(encoded.AsSpan(FixedHeaderSize + encodedPublisherLength));
            return new ByteString(encoded);
        }

        /// <summary>
        /// Validates one typed PublisherId for a configured receive association.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static void ValidatePublisherId(Variant publisherId)
        {
            _ = GetPublisherValue(publisherId);
        }

        /// <summary>
        /// Decodes the visible PublisherId from ordinary UADP or the minimal application-payload header.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static UadpNetworkMessageHeader DecodePublisherHeader(ByteString networkMessage)
        {
            if (networkMessage.IsNull || networkMessage.Length == 0)
            {
                throw new FlowCodecException(FlowCodecError.Truncated);
            }

            ReadOnlySpan<byte> bytes = networkMessage.Span;
            byte flags = bytes[0];
            if ((flags & 0x0F) != 1)
            {
                throw new FlowCodecException(FlowCodecError.InvalidApplicationHeader);
            }

            int offset = 1;
            byte extendedFlags1 = 0;
            byte extendedFlags2 = 0;
            if ((flags & ExtendedFlags1Enabled) != 0)
            {
                if (offset >= bytes.Length)
                {
                    throw new FlowCodecException(FlowCodecError.Truncated);
                }
                extendedFlags1 = bytes[offset++];
                if ((extendedFlags1 & ExtendedFlags2Enabled) != 0)
                {
                    if (offset >= bytes.Length)
                    {
                        throw new FlowCodecException(FlowCodecError.Truncated);
                    }
                    extendedFlags2 = bytes[offset++];
                    if ((extendedFlags2 & ExtendedFlags2ReservedMask) != 0)
                    {
                        throw new FlowCodecException(FlowCodecError.InvalidApplicationHeader);
                    }
                }
            }

            byte networkMessageType = (byte)((extendedFlags2 & NetworkMessageTypeMask) >> NetworkMessageTypeShift);
            if (networkMessageType == ApplicationNetworkMessageType)
            {
                UadpApplicationPayload applicationPayload = Decode(networkMessage, networkMessage.Length);
                return new UadpNetworkMessageHeader(networkMessageType, applicationPayload.PublisherId);
            }
            if (networkMessageType is not (DataSetNetworkMessageType or
                DiscoveryRequestNetworkMessageType or DiscoveryResponseNetworkMessageType))
            {
                throw new FlowCodecException(FlowCodecError.InvalidApplicationHeader);
            }

            Variant publisherId = default;
            if ((flags & PublisherIdEnabled) != 0)
            {
                byte publisherType = (byte)(extendedFlags1 & PublisherIdTypeMask);
                publisherId = ReadPublisherValue(networkMessage, publisherType, offset, out int publisherLength);
                offset = checked(offset + publisherLength);
            }

            if ((extendedFlags1 & DataSetClassIdEnabled) != 0)
            {
                RequireRemaining(bytes, offset, GuidLength);
                offset += GuidLength;
            }

            if ((extendedFlags1 & SecurityEnabled) != 0)
            {
                return new UadpNetworkMessageHeader(networkMessageType, publisherId);
            }
            if (networkMessageType == DiscoveryRequestNetworkMessageType)
            {
                ushort[] discoveryWriterIds = ReadDiscoveryRequestWriterIds(bytes, ref offset);
                return new UadpNetworkMessageHeader(networkMessageType, publisherId, dataSetWriterIds: discoveryWriterIds);
            }
            if (networkMessageType == DiscoveryResponseNetworkMessageType)
            {
                ushort[] discoveryWriterIds = ReadDiscoveryResponseWriterIds(bytes, ref offset);
                return new UadpNetworkMessageHeader(networkMessageType, publisherId, dataSetWriterIds: discoveryWriterIds);
            }

            ushort? writerGroupId = null;
            if ((flags & GroupHeaderEnabled) != 0)
            {
                RequireRemaining(bytes, offset, sizeof(byte));
                byte groupFlags = bytes[offset++];
                if ((groupFlags & GroupHeaderReservedMask) != 0)
                {
                    throw new FlowCodecException(FlowCodecError.InvalidApplicationHeader);
                }
                if ((groupFlags & WriterGroupIdEnabled) != 0)
                {
                    RequireRemaining(bytes, offset, sizeof(ushort));
                    writerGroupId = BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
                    offset += sizeof(ushort);
                }
                if ((groupFlags & GroupVersionEnabled) != 0)
                {
                    RequireRemaining(bytes, offset, sizeof(uint));
                    offset += sizeof(uint);
                }
                if ((groupFlags & NetworkMessageNumberEnabled) != 0)
                {
                    RequireRemaining(bytes, offset, sizeof(ushort));
                    offset += sizeof(ushort);
                }
                if ((groupFlags & SequenceNumberEnabled) != 0)
                {
                    RequireRemaining(bytes, offset, sizeof(ushort));
                    offset += sizeof(ushort);
                }
            }

            ushort[] dataSetWriterIds = [];
            if (networkMessageType == DataSetNetworkMessageType && (flags & PayloadHeaderEnabled) != 0)
            {
                int dataSetCount;
                if ((extendedFlags2 & ChunkMessageEnabled) != 0)
                {
                    dataSetCount = 1;
                }
                else
                {
                    RequireRemaining(bytes, offset, sizeof(byte));
                    dataSetCount = bytes[offset++];
                    if (dataSetCount == 0)
                    {
                        throw new FlowCodecException(FlowCodecError.InvalidApplicationHeader);
                    }
                }
                RequireRemaining(bytes, offset, checked(dataSetCount * sizeof(ushort)));
                dataSetWriterIds = new ushort[dataSetCount];
                for (int i = 0; i < dataSetWriterIds.Length; i++)
                {
                    dataSetWriterIds[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
                    offset += sizeof(ushort);
                }
            }

            if ((extendedFlags1 & TimestampEnabled) != 0)
            {
                RequireRemaining(bytes, offset, DateTimeLength);
                offset += DateTimeLength;
            }
            if ((extendedFlags1 & PicoSecondsEnabled) != 0)
            {
                RequireRemaining(bytes, offset, sizeof(ushort));
                offset += sizeof(ushort);
            }

            return new UadpNetworkMessageHeader(
                networkMessageType,
                publisherId,
                writerGroupId,
                dataSetWriterIds);
        }

        /// <summary>
        /// Extracts writer identities from the common UADP discovery-request body prefix.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static ushort[] ReadDiscoveryRequestWriterIds(ReadOnlySpan<byte> bytes, ref int offset)
        {
            RequireRemaining(bytes, offset, sizeof(byte) + sizeof(uint));
            offset++;
            return ReadDiscoveryWriterIdList(bytes, ref offset);
        }

        /// <summary>
        /// Extracts writer identities exposed by metadata and writer-configuration responses.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static ushort[] ReadDiscoveryResponseWriterIds(ReadOnlySpan<byte> bytes, ref int offset)
        {
            RequireRemaining(bytes, offset, sizeof(byte) + sizeof(ushort));
            byte discoveryType = bytes[offset++];
            offset += sizeof(ushort);
            if (discoveryType == DataSetMetaDataDiscoveryType)
            {
                RequireRemaining(bytes, offset, sizeof(ushort));
                ushort writerId = BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
                return [writerId];
            }
            if (discoveryType == DataSetWriterConfigurationDiscoveryType)
            {
                return ReadDiscoveryWriterIdList(bytes, ref offset);
            }
            return [];
        }

        /// <summary>
        /// Reads a UInt32-counted array of DataSetWriterIds without allocating from an unbounded count.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static ushort[] ReadDiscoveryWriterIdList(ReadOnlySpan<byte> bytes, ref int offset)
        {
            RequireRemaining(bytes, offset, sizeof(uint));
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
            offset += sizeof(uint);
            int availableCount = (bytes.Length - offset) / sizeof(ushort);
            if (count > (uint)availableCount)
            {
                throw new FlowCodecException(FlowCodecError.Truncated);
            }
            var writerIds = new ushort[(int)count];
            for (int i = 0; i < writerIds.Length; i++)
            {
                writerIds[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
                offset += sizeof(ushort);
            }
            return writerIds;
        }

        /// <summary>
        /// Decodes exactly the amended minimal header and borrows the remaining payload bytes.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static UadpApplicationPayload Decode(ByteString networkMessage, int maximumMessageBytes)
        {
            if (maximumMessageBytes < FixedHeaderSize)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumMessageBytes));
            }
            if (networkMessage.IsNull)
            {
                throw new FlowCodecException(FlowCodecError.WrongType);
            }
            if (networkMessage.Length > maximumMessageBytes)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }
            if (networkMessage.Length < FixedHeaderSize)
            {
                throw new FlowCodecException(FlowCodecError.Truncated);
            }

            ReadOnlySpan<byte> bytes = networkMessage.Span;
            if (bytes[0] != UadpFlags || bytes[2] != ApplicationNetworkMessageTypeBits)
            {
                throw new FlowCodecException(FlowCodecError.InvalidApplicationHeader);
            }

            byte extendedFlags1 = bytes[1];
            if ((extendedFlags1 & ExtendedFlags1ReservedMask) != 0 ||
                (extendedFlags1 & ExtendedFlags1Enabled) == 0)
            {
                throw new FlowCodecException(FlowCodecError.InvalidApplicationHeader);
            }
            byte publisherType = (byte)(extendedFlags1 & PublisherIdTypeMask);
            Variant publisherId = ReadPublisherValue(networkMessage, publisherType, out int publisherLength);
            return new UadpApplicationPayload(publisherId, networkMessage[(FixedHeaderSize + publisherLength)..]);
        }

        /// <summary>
        /// Holds a validated typed PublisherId without boxing numeric values.
        /// </summary>
        private readonly struct PublisherValue
        {
            /// <summary>
            /// Creates a typed publisher value.
            /// </summary>
            internal PublisherValue(byte type, ulong numeric, string? text)
            {
                Type = type;
                Numeric = numeric;
                Text = text;
            }

            /// <summary>
            /// Gets the ExtendedFlags1 PublisherId type discriminator.
            /// </summary>
            internal byte Type { get; }

            /// <summary>
            /// Gets the unsigned numeric value.
            /// </summary>
            internal ulong Numeric { get; }

            /// <summary>
            /// Gets the string value.
            /// </summary>
            internal string? Text { get; }
        }

        /// <summary>
        /// Validates and extracts one supported typed PublisherId.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static PublisherValue GetPublisherValue(Variant publisherId)
        {
            if (publisherId.TryGetValue(out byte byteValue) && byteValue != 0)
            {
                return new PublisherValue(PublisherTypeByte, byteValue, null);
            }
            if (publisherId.TryGetValue(out ushort ushortValue) && ushortValue != 0)
            {
                return new PublisherValue(PublisherTypeUInt16, ushortValue, null);
            }
            if (publisherId.TryGetValue(out uint uintValue) && uintValue != 0)
            {
                return new PublisherValue(PublisherTypeUInt32, uintValue, null);
            }
            if (publisherId.TryGetValue(out ulong ulongValue) && ulongValue != 0)
            {
                return new PublisherValue(PublisherTypeUInt64, ulongValue, null);
            }
            if (publisherId.TryGetValue(out string text) && text is not null && text.Length > 0)
            {
                try
                {
                    StrictUtf8.GetByteCount(text);
                }
                catch (EncoderFallbackException)
                {
                    throw new FlowCodecException(FlowCodecError.InvalidUnicode);
                }
                return new PublisherValue(PublisherTypeString, 0, text);
            }
            throw new FlowCodecException(FlowCodecError.InvalidPublisherId);
        }

        /// <summary>
        /// Gets the byte length of one validated typed PublisherId.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static int GetPublisherLength(PublisherValue value)
        {
            return value.Type switch
            {
                PublisherTypeByte => 1,
                PublisherTypeUInt16 => sizeof(ushort),
                PublisherTypeUInt32 => sizeof(uint),
                PublisherTypeUInt64 => sizeof(ulong),
                PublisherTypeString => checked(sizeof(int) + StrictUtf8.GetByteCount(value.Text!)),
                _ => throw new FlowCodecException(FlowCodecError.InvalidPublisherIdType)
            };
        }

        /// <summary>
        /// Writes a typed PublisherId using OPC UA Binary field encodings.
        /// </summary>
        private static void WritePublisherValue(PublisherValue value, byte[] destination, int offset)
        {
            switch (value.Type)
            {
                case PublisherTypeByte:
                    destination[offset] = (byte)value.Numeric;
                    break;
                case PublisherTypeUInt16:
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        destination.AsSpan(offset, sizeof(ushort)), (ushort)value.Numeric);
                    break;
                case PublisherTypeUInt32:
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        destination.AsSpan(offset, sizeof(uint)), (uint)value.Numeric);
                    break;
                case PublisherTypeUInt64:
                    BinaryPrimitives.WriteUInt64LittleEndian(
                        destination.AsSpan(offset, sizeof(ulong)), value.Numeric);
                    break;
                case PublisherTypeString:
                    int byteCount = StrictUtf8.GetByteCount(value.Text!);
                    BinaryPrimitives.WriteInt32LittleEndian(
                        destination.AsSpan(offset, sizeof(int)), byteCount);
                    _ = StrictUtf8.GetBytes(
                        value.Text!, 0, value.Text!.Length, destination, offset + sizeof(int));
                    break;
                default:
                    throw new FlowCodecException(FlowCodecError.InvalidPublisherIdType);
            }
        }

        /// <summary>
        /// Reads a typed PublisherId and its encoded length from the NetworkMessage.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static Variant ReadPublisherValue(ByteString networkMessage, byte type, out int length)
        {
            return ReadPublisherValue(networkMessage, type, FixedHeaderSize, out length);
        }

        /// <summary>
        /// Reads a typed PublisherId and its encoded length from the NetworkMessage.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static Variant ReadPublisherValue(
            ByteString networkMessage,
            byte type,
            int offset,
            out int length)
        {
            ReadOnlySpan<byte> bytes = networkMessage.Span;
            ReadOnlySpan<byte> value = bytes[offset..];
            switch (type)
            {
                case PublisherTypeByte:
                    RequireLength(value, sizeof(byte));
                    length = sizeof(byte);
                    if (value[0] == 0)
                    {
                        throw new FlowCodecException(FlowCodecError.InvalidPublisherId);
                    }
                    return new Variant(value[0]);
                case PublisherTypeUInt16:
                    RequireLength(value, sizeof(ushort));
                    length = sizeof(ushort);
                    ushort ushortValue = BinaryPrimitives.ReadUInt16LittleEndian(value);
                    if (ushortValue == 0)
                    {
                        throw new FlowCodecException(FlowCodecError.InvalidPublisherId);
                    }
                    return new Variant(ushortValue);
                case PublisherTypeUInt32:
                    RequireLength(value, sizeof(uint));
                    length = sizeof(uint);
                    uint uintValue = BinaryPrimitives.ReadUInt32LittleEndian(value);
                    if (uintValue == 0)
                    {
                        throw new FlowCodecException(FlowCodecError.InvalidPublisherId);
                    }
                    return new Variant(uintValue);
                case PublisherTypeUInt64:
                    RequireLength(value, sizeof(ulong));
                    length = sizeof(ulong);
                    ulong ulongValue = BinaryPrimitives.ReadUInt64LittleEndian(value);
                    if (ulongValue == 0)
                    {
                        throw new FlowCodecException(FlowCodecError.InvalidPublisherId);
                    }
                    return new Variant(ulongValue);
                case PublisherTypeString:
                    return ReadPublisherString(networkMessage, offset, out length);
                default:
                    throw new FlowCodecException(FlowCodecError.InvalidPublisherIdType);
            }
        }

        /// <summary>
        /// Reads a nonempty UTF-8 PublisherId without allowing the declared length to exceed the message.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static Variant ReadPublisherString(ByteString networkMessage, int offset, out int length)
        {
            ReadOnlySpan<byte> value = networkMessage.Span[offset..];
            RequireLength(value, sizeof(int));
            int byteCount = BinaryPrimitives.ReadInt32LittleEndian(value);
            if (byteCount <= 0)
            {
                throw new FlowCodecException(FlowCodecError.InvalidPublisherId);
            }
            if (byteCount > value.Length - sizeof(int))
            {
                throw new FlowCodecException(FlowCodecError.Truncated);
            }
            try
            {
                ReadOnlyMemory<byte> textBytes = networkMessage.Memory.Slice(offset + sizeof(int), byteCount);
                string text = MemoryMarshal.TryGetArray(textBytes, out ArraySegment<byte> segment)
                    ? StrictUtf8.GetString(segment.Array!, segment.Offset, segment.Count)
                    : StrictUtf8.GetString(textBytes.ToArray());
                length = checked(sizeof(int) + byteCount);
                return new Variant(text);
            }
            catch (DecoderFallbackException)
            {
                throw new FlowCodecException(FlowCodecError.InvalidUtf8);
            }
        }

        /// <summary>
        /// Rejects a typed value that is shorter than its fixed encoding.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static void RequireLength(ReadOnlySpan<byte> value, int requiredLength)
        {
            if (value.Length < requiredLength)
            {
                throw new FlowCodecException(FlowCodecError.Truncated);
            }
        }

        /// <summary>
        /// Rejects an optional header field that extends past the NetworkMessage.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static void RequireRemaining(ReadOnlySpan<byte> bytes, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset > bytes.Length - length)
            {
                throw new FlowCodecException(FlowCodecError.Truncated);
            }
        }

        private static readonly UTF8Encoding StrictUtf8 = new(false, true);
        private const byte UadpFlags = 0x91;
        private const byte PublisherIdEnabled = 0x10;
        private const byte GroupHeaderEnabled = 0x20;
        private const byte PayloadHeaderEnabled = 0x40;
        private const byte ExtendedFlags1Enabled = 0x80;
        private const byte ExtendedFlags2Enabled = 0x80;
        private const byte ExtendedFlags1ReservedMask = 0x78;
        private const byte ExtendedFlags2ReservedMask = 0xC0;
        private const byte PublisherIdTypeMask = 0x07;
        private const byte DataSetClassIdEnabled = 0x08;
        private const byte SecurityEnabled = 0x10;
        private const byte TimestampEnabled = 0x20;
        private const byte PicoSecondsEnabled = 0x40;
        private const byte WriterGroupIdEnabled = 0x01;
        private const byte GroupVersionEnabled = 0x02;
        private const byte NetworkMessageNumberEnabled = 0x04;
        private const byte SequenceNumberEnabled = 0x08;
        private const byte GroupHeaderReservedMask = 0xF0;
        private const byte ChunkMessageEnabled = 0x01;
        private const byte NetworkMessageTypeMask = 0x1C;
        private const int NetworkMessageTypeShift = 2;
        private const byte DataSetNetworkMessageType = 0;
        private const byte DiscoveryRequestNetworkMessageType = 1;
        private const byte DiscoveryResponseNetworkMessageType = 2;
        private const byte DataSetMetaDataDiscoveryType = 2;
        private const byte DataSetWriterConfigurationDiscoveryType = 3;
        private const byte ApplicationNetworkMessageType = 3;
        private const byte ApplicationNetworkMessageTypeBits = 0x0C;
        private const byte PublisherTypeByte = 0;
        private const byte PublisherTypeUInt16 = 1;
        private const byte PublisherTypeUInt32 = 2;
        private const byte PublisherTypeUInt64 = 3;
        private const byte PublisherTypeString = 4;
        private const int FixedHeaderSize = 3;
        private const int GuidLength = 16;
        private const int DateTimeLength = sizeof(long);
    }
}
