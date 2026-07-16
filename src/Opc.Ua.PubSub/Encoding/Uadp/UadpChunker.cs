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
using System.Collections.Generic;

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// Splits an encoded UADP NetworkMessage into wire-bounded chunks
    /// and re-emits them as self-contained chunk frames.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.4">
    /// Part 14 §7.2.4.4.4 ChunkedNetworkMessage</see>. Each emitted
    /// chunk frame carries a 10-byte chunk header
    /// <code>(MessageSequenceNumber UInt16 + ChunkOffset UInt32 +
    /// TotalSize UInt32)</code> followed by the chunk payload.
    /// </remarks>
    public sealed class UadpChunker
    {
        /// <summary>
        /// Size of the chunk header that prefixes each chunk
        /// payload.
        /// </summary>
        public const int ChunkHeaderSize = 10;

        /// <summary>
        /// Splits the supplied encoded NetworkMessage into chunks. The
        /// caller is expected to send each returned <c>byte[]</c> as a
        /// single transport frame.
        /// </summary>
        /// <param name="encodedMessage">The complete encoded
        /// NetworkMessage bytes to split.</param>
        /// <param name="messageSequenceNumber">The sequence number of
        /// the source NetworkMessage carried in each chunk header.
        /// </param>
        /// <param name="maxFrameSize">Maximum size (in bytes) of one
        /// transport frame including the chunk header.</param>
        /// <returns>An ordered, non-empty list of chunk frames covering
        /// the full message. When the message fits within
        /// <paramref name="maxFrameSize"/> minus the chunk header the
        /// list contains exactly one element.</returns>
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
                writer.WriteBytes(source.Slice(offset, payloadSize));
                chunks.Add(chunk);
            }

            return chunks;
        }

        /// <summary>
        /// Reads the chunk header from the supplied frame.
        /// </summary>
        /// <param name="frame">A single chunk frame produced by
        /// <see cref="Split"/>.</param>
        /// <param name="messageSequenceNumber">The decoded
        /// MessageSequenceNumber when this method returns
        /// <c>true</c>.</param>
        /// <param name="chunkOffset">The decoded byte offset of the
        /// chunk payload inside the original message.</param>
        /// <param name="totalSize">The decoded total size of the
        /// original message.</param>
        /// <param name="payload">The chunk payload bytes (the slice of
        /// <paramref name="frame"/> following the 10-byte header).
        /// </param>
        /// <returns><c>true</c> when the chunk header could be parsed;
        /// <c>false</c> when the frame is too short.</returns>
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
            messageSequenceNumber = (ushort)(span[0] | (span[1] << 8));
            chunkOffset = (uint)(span[2] |
                (span[3] << 8) |
                (span[4] << 16) |
                (span[5] << 24));
            totalSize = (uint)(span[6] |
                (span[7] << 8) |
                (span[8] << 16) |
                (span[9] << 24));
            payload = frame[ChunkHeaderSize..];
            return true;
        }
    }
}
