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
using System.Collections.Generic;
using System.IO;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;

namespace Opc.Ua.Fuzzing
{
    public static partial class FuzzableCode
    {
        public static void AflfuzzUadpNetworkMessageDecode(Stream stream)
        {
            _ = DecodeUadp(ReadCapped(stream), NewContext());
        }

        public static void LibfuzzUadpNetworkMessageDecode(ReadOnlySpan<byte> input)
        {
            _ = DecodeUadp(CopyCapped(input), NewContext());
        }

        public static void AflfuzzUadpChunkReassembly(Stream stream)
        {
            ExerciseUadpChunks(ReadCapped(stream), NewContext());
        }

        public static void LibfuzzUadpChunkReassembly(ReadOnlySpan<byte> input)
        {
            ExerciseUadpChunks(CopyCapped(input), NewContext());
        }

        internal static PubSubNetworkMessage DecodeUadp(
            ReadOnlyMemory<byte> input,
            PubSubNetworkMessageContext context)
        {
            return UadpDecoder.Decode(input, context);
        }

        internal static void ExerciseUadpChunks(
            ReadOnlyMemory<byte> input,
            PubSubNetworkMessageContext context)
        {
            // Untrusted chunk headers must not poison the state used by the
            // independently generated, valid split/reassemble oracle.
            using (var probe = new UadpReassembler(context.TimeProvider))
            {
                if (probe.TryAddChunk(SeedPublisherId, 1, input, out ReadOnlyMemory<byte>? decodedChunk))
                {
                    ReadOnlyMemory<byte> complete = decodedChunk
                        ?? throw new InvalidOperationException("Completed chunk did not return a message.");
                    if (!complete.Span.SequenceEqual(input.Span[UadpChunker.ChunkHeaderSize..]) ||
                        probe.PendingCount != 0)
                    {
                        throw new InvalidOperationException("Single-chunk reassembly changed its payload or state.");
                    }
                    _ = DecodeUadp(complete, context);
                }
            }

            if (!input.IsEmpty)
            {
                ReadOnlyMemory<byte> ordered = ReassembleUadpPayload(input, context.TimeProvider, reverse: false);
                _ = ReassembleUadpPayload(input, context.TimeProvider, reverse: true);
                _ = DecodeUadp(ordered, context);
            }
        }

        internal static ReadOnlyMemory<byte> ReassembleUadpPayload(
            ReadOnlyMemory<byte> input,
            TimeProvider timeProvider,
            bool reverse,
            int maxFrameSize = 256)
        {
            IReadOnlyList<byte[]> chunks = new UadpChunker().Split(input, 42, maxFrameSize);
            using var reassembler = new UadpReassembler(timeProvider);
            ReadOnlyMemory<byte>? reassembled = null;
            for (int i = 0; i < chunks.Count; i++)
            {
                byte[] chunk = chunks[reverse ? chunks.Count - 1 - i : i];
                bool complete = reassembler.TryAddChunk(SeedPublisherId, 1, chunk, out reassembled);
                if (complete != (i == chunks.Count - 1))
                {
                    throw new InvalidOperationException("Reassembly completed at the wrong chunk boundary.");
                }
                if (i == 0 && chunks.Count > 1 &&
                    (reassembler.TryAddChunk(SeedPublisherId, 1, chunk, out ReadOnlyMemory<byte>? duplicate) ||
                        duplicate is not null || reassembler.PendingCount != 1))
                {
                    throw new InvalidOperationException("A duplicate chunk changed the incomplete reassembly.");
                }
            }

            ReadOnlyMemory<byte> result = reassembled
                ?? throw new InvalidOperationException("Valid chunks did not reassemble.");
            if (!result.Span.SequenceEqual(input.Span) || reassembler.PendingCount != 0)
            {
                throw new InvalidOperationException("Reassembly changed the input or retained completed state.");
            }
            return result;
        }
    }
}
