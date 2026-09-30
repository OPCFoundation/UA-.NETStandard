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
 *
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
using System.Collections.Generic;

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// Splits a UADP payload into the chunk payload fields of Part 14
    /// Table 159 and builds / parses UADP chunk NetworkMessages.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.4">
    /// Part 14 §7.2.4.4.4 UADP Chunk NetworkMessage</see>. A chunk
    /// NetworkMessage carries the regular NetworkMessage header with the
    /// ExtendedFlags2 chunk bit, a PayloadHeader holding only the
    /// DataSetWriterId (Table 158) and the payload
    /// <code>(MessageSequenceNumber UInt16, ChunkOffset UInt32,
    /// TotalSize UInt32, ChunkData ByteString)</code> (Table 159). Each
    /// chunk carries a piece of exactly one DataSetMessage and is secured
    /// on its own; all chunks except the last one have the same size.
    /// </remarks>
    public sealed class UadpChunker
    {
        /// <summary>
        /// Size of the chunk payload fields that precede the ChunkData
        /// bytes: MessageSequenceNumber, ChunkOffset, TotalSize and the
        /// Int32 length of the ChunkData ByteString.
        /// </summary>
        public const int ChunkHeaderSize = 14;

        /// <summary>
        /// Splits the supplied payload (a DataSetMessage or a discovery
        /// announcement payload) into chunk payloads. The caller places
        /// each returned <c>byte[]</c> behind a chunk NetworkMessage header.
        /// </summary>
        /// <param name="encodedMessage">The complete payload bytes to
        /// split.</param>
        /// <param name="messageSequenceNumber">The MessageSequenceNumber
        /// carried in each chunk.</param>
        /// <param name="maxFrameSize">Maximum size (in bytes) of one chunk
        /// payload including the <see cref="ChunkHeaderSize"/> fields.</param>
        /// <returns>An ordered, non-empty list of chunk payloads covering
        /// the full payload. All chunks except the last one have the same
        /// size.</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public IReadOnlyList<byte[]> Split(
            ReadOnlyMemory<byte> encodedMessage,
            ushort messageSequenceNumber,
            int maxFrameSize)
        {
            if (encodedMessage.Length == 0)
            {
                throw new ArgumentException(
                    "Encoded message must not be empty.",
                    nameof(encodedMessage));
            }
            if (maxFrameSize <= ChunkHeaderSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxFrameSize),
                    "maxFrameSize must be greater than the chunk header size.");
            }

            int chunkPayloadSize = maxFrameSize - ChunkHeaderSize;
            int totalSize = encodedMessage.Length;
            int chunkCount = (totalSize + chunkPayloadSize - 1) / chunkPayloadSize;
            var chunks = new List<byte[]>(chunkCount);
            ReadOnlySpan<byte> source = encodedMessage.Span;

            for (int i = 0; i < chunkCount; i++)
            {
                int offset = i * chunkPayloadSize;
                int remaining = totalSize - offset;
                int payloadSize = remaining < chunkPayloadSize
                    ? remaining
                    : chunkPayloadSize;

                byte[] chunk = new byte[ChunkHeaderSize + payloadSize];
                var writer = new UadpBinaryWriter(chunk, 0, chunk.Length);
                writer.WriteUInt16Le(messageSequenceNumber);
                writer.WriteUInt32Le((uint)offset);
                writer.WriteUInt32Le((uint)totalSize);
                writer.WriteUInt32Le((uint)payloadSize);
                writer.WriteBytes(source.Slice(offset, payloadSize));
                chunks.Add(chunk);
            }

            return chunks;
        }

        /// <summary>
        /// Reads the chunk payload fields (Part 14 Table 159).
        /// </summary>
        /// <param name="frame">A chunk payload as produced by
        /// <see cref="Split"/>.</param>
        /// <param name="messageSequenceNumber">The decoded
        /// MessageSequenceNumber when this method returns
        /// <c>true</c>.</param>
        /// <param name="chunkOffset">The decoded byte offset of the
        /// chunk inside the complete payload.</param>
        /// <param name="totalSize">The decoded total size of the
        /// complete payload.</param>
        /// <param name="payload">The ChunkData bytes.</param>
        /// <returns><c>true</c> when the chunk fields could be parsed;
        /// <c>false</c> when the frame is truncated, the ChunkData length
        /// is negative or does not match the remaining bytes.</returns>
        public static bool TryParseChunk(
            ReadOnlyMemory<byte> frame,
            out ushort messageSequenceNumber,
            out uint chunkOffset,
            out uint totalSize,
            out ReadOnlyMemory<byte> payload)
        {
            messageSequenceNumber = 0;
            chunkOffset = 0;
            totalSize = 0;
            payload = ReadOnlyMemory<byte>.Empty;

            if (frame.Length < ChunkHeaderSize)
            {
                return false;
            }

            ReadOnlySpan<byte> span = frame.Span;
            int dataLength = BinaryPrimitives.ReadInt32LittleEndian(span[10..]);
            if (dataLength < 0 || dataLength != frame.Length - ChunkHeaderSize)
            {
                return false;
            }
            messageSequenceNumber = BinaryPrimitives.ReadUInt16LittleEndian(span);
            chunkOffset = BinaryPrimitives.ReadUInt32LittleEndian(span[2..]);
            totalSize = BinaryPrimitives.ReadUInt32LittleEndian(span[6..]);
            payload = frame.Slice(ChunkHeaderSize, dataLength);
            return true;
        }

        /// <summary>
        /// Splits an encoded, unsecured DataSetMessage NetworkMessage into
        /// chunk NetworkMessages, one chunk series per DataSetMessage.
        /// </summary>
        /// <param name="frame">Encoded NetworkMessage whose payload is in
        /// cleartext (no SecurityHeader).</param>
        /// <param name="maxNetworkMessageSize">Maximum size of one chunk
        /// NetworkMessage on the wire.</param>
        /// <param name="securityOverhead">Bytes reserved per chunk for the
        /// SecurityHeader and signature added when the chunk is secured.</param>
        /// <param name="securityEnabled">Set the ExtendedFlags1
        /// SecurityHeader bit in each chunk header; the caller secures each
        /// chunk at <see cref="UadpChunkFrame.PayloadOffset"/>.</param>
        /// <param name="fallbackSequenceNumber">MessageSequenceNumber used
        /// for DataSetMessages that carry no sequence number.</param>
        /// <param name="reserveNetworkSequenceNumbers">Reserves the given
        /// number of consecutive GroupHeader SequenceNumbers from the
        /// WriterGroup counter and returns the first one. The first chunk
        /// keeps the SequenceNumber of <paramref name="frame"/>; every
        /// further chunk NetworkMessage takes a reserved one. When
        /// <c>null</c> the chunks continue from the SequenceNumber of
        /// <paramref name="frame"/>.</param>
        /// <returns>The chunk NetworkMessages, or <c>null</c> when the frame
        /// is not an unsecured DataSetMessage NetworkMessage that can be
        /// split.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="maxNetworkMessageSize"/> leaves no room for chunk
        /// data.</exception>
        internal static IReadOnlyList<UadpChunkFrame>? TrySplitNetworkMessage(
            ReadOnlyMemory<byte> frame,
            int maxNetworkMessageSize,
            int securityOverhead,
            bool securityEnabled,
            ushort fallbackSequenceNumber,
            Func<int, ushort>? reserveNetworkSequenceNumbers = null)
        {
            if (!UadpDecoder.TryReadPrefix(frame, out UadpPrefixInfo info) ||
                info.ChunkMessage ||
                info.SecurityEnabled ||
                !info.IsDataSetMessage)
            {
                return null;
            }

            ReadOnlySpan<byte> span = frame.Span;
            bool hasPayloadHeader =
                (info.UadpFlags & UadpFlagsEncodingMask.PayloadHeaderEnabled) != 0;
            int count = hasPayloadHeader ? info.PayloadCount : 1;
            int position = info.PrefixLength;
            int[] sizes = new int[count];
            if (count > 1)
            {
                if (span.Length - position < 2 * count)
                {
                    return null;
                }
                int total = 0;
                for (int i = 0; i < count; i++)
                {
                    sizes[i] = BinaryPrimitives.ReadUInt16LittleEndian(span[(position + (2 * i))..]);
                    total += sizes[i];
                    if (sizes[i] == 0)
                    {
                        return null;
                    }
                }
                position += 2 * count;
                if (total > span.Length - position)
                {
                    return null;
                }
            }
            else
            {
                sizes[0] = span.Length - position;
                if (sizes[0] == 0)
                {
                    return null;
                }
            }

            var chunker = new UadpChunker();
            var frames = new List<byte[]>();
            var payloadOffsets = new List<int>();
            for (int i = 0; i < count; i++)
            {
                ushort? writerId = hasPayloadHeader
                    ? BinaryPrimitives.ReadUInt16LittleEndian(
                        span[(info.PayloadHeaderOffset + 1 + (2 * i))..])
                    : null;
                ReadOnlyMemory<byte> dataSetMessage = frame.Slice(position, sizes[i]);
                position += sizes[i];

                byte[] prefix = BuildChunkPrefix(span, info, writerId, securityEnabled);
                ushort sequenceNumber = TryReadDataSetMessageSequenceNumber(
                    dataSetMessage.Span, out ushort dsmSequence)
                    ? dsmSequence
                    : fallbackSequenceNumber;
                IReadOnlyList<byte[]> pieces = chunker.Split(
                    dataSetMessage,
                    sequenceNumber,
                    maxNetworkMessageSize - prefix.Length - securityOverhead);
                foreach (byte[] piece in pieces)
                {
                    byte[] chunkFrame = new byte[prefix.Length + piece.Length];
                    Buffer.BlockCopy(prefix, 0, chunkFrame, 0, prefix.Length);
                    Buffer.BlockCopy(piece, 0, chunkFrame, prefix.Length, piece.Length);
                    frames.Add(chunkFrame);
                    payloadOffsets.Add(prefix.Length);
                }
            }
            NumberChunkNetworkMessages(frames, span, info, reserveNetworkSequenceNumbers);
            var result = new List<UadpChunkFrame>(frames.Count);
            for (int i = 0; i < frames.Count; i++)
            {
                result.Add(new UadpChunkFrame(frames[i], payloadOffsets[i]));
            }
            return result;
        }

        /// <summary>
        /// Gives every chunk NetworkMessage its own GroupHeader
        /// SequenceNumber ("Sequence number for each new NetworkMessage",
        /// Part 14 §7.2.4.4.2 Table 154, incremented by exactly one per
        /// message, §7.2.3) and an incrementing NetworkMessageNumber
        /// (the NetworkMessages a WriterGroup chunks over within one
        /// PublishingInterval are numbered consecutively, §6.3.1.3.4).
        /// </summary>
        private static void NumberChunkNetworkMessages(
            List<byte[]> chunks,
            ReadOnlySpan<byte> frame,
            in UadpPrefixInfo info,
            Func<int, ushort>? reserveNetworkSequenceNumbers)
        {
            if (chunks.Count < 2)
            {
                return;
            }
            // The GroupHeader precedes the PayloadHeader and is copied into
            // each chunk prefix right behind the three flag bytes.
            int shift = 3 - info.FlagsLength;
            if (info.SequenceNumberOffset >= 0)
            {
                ushort first = BinaryPrimitives.ReadUInt16LittleEndian(
                    frame[info.SequenceNumberOffset..]);
                ushort next = reserveNetworkSequenceNumbers is null
                    ? unchecked((ushort)(first + 1))
                    : reserveNetworkSequenceNumbers(chunks.Count - 1);
                for (int i = 1; i < chunks.Count; i++)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        chunks[i].AsSpan(info.SequenceNumberOffset + shift), next);
                    next = unchecked((ushort)(next + 1));
                }
            }
            if (info.NetworkMessageNumberOffset >= 0)
            {
                ushort first = BinaryPrimitives.ReadUInt16LittleEndian(
                    frame[info.NetworkMessageNumberOffset..]);
                for (int i = 1; i < chunks.Count; i++)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        chunks[i].AsSpan(info.NetworkMessageNumberOffset + shift),
                        unchecked((ushort)(first + i)));
                }
            }
        }

        /// <summary>
        /// Builds the cleartext, non-chunk NetworkMessage for a payload
        /// reassembled from chunk NetworkMessages: the header of the
        /// completing chunk without the chunk and security bits, a
        /// PayloadHeader with Count 1 and the chunk's DataSetWriterId,
        /// followed by the reassembled payload.
        /// </summary>
        /// <param name="chunkFrame">The completing chunk NetworkMessage
        /// (at least its prefix).</param>
        /// <param name="info">The parsed prefix of
        /// <paramref name="chunkFrame"/>.</param>
        /// <param name="payload">The reassembled DataSetMessage or discovery
        /// announcement payload.</param>
        internal static byte[] ComposeReassembledNetworkMessage(
            ReadOnlySpan<byte> chunkFrame,
            in UadpPrefixInfo info,
            ReadOnlySpan<byte> payload)
        {
            UadpFlagsEncodingMask uadpFlags = info.UadpFlags;
            ExtendedFlags1EncodingMask ext1 = info.ExtendedFlags1 &
                ~ExtendedFlags1EncodingMask.SecurityEnabled;
            ExtendedFlags2EncodingMask ext2 = info.ExtendedFlags2 &
                ~ExtendedFlags2EncodingMask.ChunkMessage;

            int headerEnd = info.PayloadHeaderOffset >= 0
                ? info.PayloadHeaderOffset
                : info.PrefixLength;
            ReadOnlySpan<byte> head = chunkFrame[info.FlagsLength..headerEnd];
            ReadOnlySpan<byte> tail = info.PayloadHeaderOffset >= 0
                ? chunkFrame[(info.PayloadHeaderOffset + info.PayloadHeaderLength)..info.PrefixLength]
                : [];
            int payloadHeaderLength = info.ChunkDataSetWriterId.HasValue ? 3 : 0;

            var flags = new List<byte>(3);
            WriteFlags(flags, uadpFlags, ext1, ext2);
            byte[] result = new byte[
                flags.Count + head.Length + payloadHeaderLength + tail.Length + payload.Length];
            int position = 0;
            foreach (byte flag in flags)
            {
                result[position++] = flag;
            }
            head.CopyTo(result.AsSpan(position));
            position += head.Length;
            if (info.ChunkDataSetWriterId is ushort writerId)
            {
                result[position++] = 1;
                BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(position), writerId);
                position += 2;
            }
            tail.CopyTo(result.AsSpan(position));
            position += tail.Length;
            payload.CopyTo(result.AsSpan(position));
            return result;
        }

        private static byte[] BuildChunkPrefix(
            ReadOnlySpan<byte> frame,
            in UadpPrefixInfo info,
            ushort? writerId,
            bool securityEnabled)
        {
            UadpFlagsEncodingMask uadpFlags = info.UadpFlags |
                UadpFlagsEncodingMask.ExtendedFlags1Enabled;
            ExtendedFlags1EncodingMask ext1 = info.ExtendedFlags1 |
                ExtendedFlags1EncodingMask.ExtendedFlags2Enabled;
            if (securityEnabled)
            {
                ext1 |= ExtendedFlags1EncodingMask.SecurityEnabled;
            }
            ExtendedFlags2EncodingMask ext2 = info.ExtendedFlags2 |
                ExtendedFlags2EncodingMask.ChunkMessage;

            ReadOnlySpan<byte> head = frame[info.FlagsLength..info.PayloadHeaderOffset];
            ReadOnlySpan<byte> tail =
                frame[(info.PayloadHeaderOffset + info.PayloadHeaderLength)..info.PrefixLength];
            int payloadHeaderLength = writerId.HasValue ? 2 : 0;
            byte[] prefix = new byte[3 + head.Length + payloadHeaderLength + tail.Length];
            prefix[0] = ((byte)1).Combine(uadpFlags);
            prefix[1] = (byte)ext1;
            prefix[2] = (byte)ext2;
            int position = 3;
            head.CopyTo(prefix.AsSpan(position));
            position += head.Length;
            if (writerId is ushort id)
            {
                // Table 158: the chunk PayloadHeader is the DataSetWriterId.
                BinaryPrimitives.WriteUInt16LittleEndian(prefix.AsSpan(position), id);
                position += 2;
            }
            tail.CopyTo(prefix.AsSpan(position));
            return prefix;
        }

        private static void WriteFlags(
            List<byte> flags,
            UadpFlagsEncodingMask uadpFlags,
            ExtendedFlags1EncodingMask ext1,
            ExtendedFlags2EncodingMask ext2)
        {
            // Omit extended flag bytes that became zero (Table 154).
            if (ext2 == 0)
            {
                ext1 &= ~ExtendedFlags1EncodingMask.ExtendedFlags2Enabled;
            }
            if (ext1 == 0)
            {
                uadpFlags &= ~UadpFlagsEncodingMask.ExtendedFlags1Enabled;
            }
            flags.Add(((byte)1).Combine(uadpFlags));
            if ((uadpFlags & UadpFlagsEncodingMask.ExtendedFlags1Enabled) != 0)
            {
                flags.Add((byte)ext1);
                if ((ext1 & ExtendedFlags1EncodingMask.ExtendedFlags2Enabled) != 0)
                {
                    flags.Add((byte)ext2);
                }
            }
        }

        private static bool TryReadDataSetMessageSequenceNumber(
            ReadOnlySpan<byte> dataSetMessage,
            out ushort sequenceNumber)
        {
            sequenceNumber = 0;
            if (dataSetMessage.IsEmpty)
            {
                return false;
            }
            var flags1 = (DataSetFlags1EncodingMask)dataSetMessage[0];
            if ((flags1 & DataSetFlags1EncodingMask.SequenceNumberEnabled) == 0)
            {
                return false;
            }
            int offset = (flags1 & DataSetFlags1EncodingMask.DataSetFlags2Enabled) != 0 ? 2 : 1;
            if (dataSetMessage.Length < offset + 2)
            {
                return false;
            }
            sequenceNumber = BinaryPrimitives.ReadUInt16LittleEndian(dataSetMessage[offset..]);
            return true;
        }
    }

    /// <summary>
    /// One encoded chunk NetworkMessage and the offset at which its
    /// payload (Part 14 Table 159) starts, i.e. where the SecurityHeader
    /// is inserted when the chunk is secured.
    /// </summary>
    internal readonly record struct UadpChunkFrame(ReadOnlyMemory<byte> Frame, int PayloadOffset);
}
