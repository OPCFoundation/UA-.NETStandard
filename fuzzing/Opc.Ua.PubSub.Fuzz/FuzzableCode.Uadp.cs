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
using System.IO;
using Opc.Ua.PubSub.Encoding.Uadp;

namespace Opc.Ua.Fuzzing
{
    public static partial class FuzzableCode
    {
        public static void AflfuzzUadpNetworkMessageDecode(Stream stream)
        {
            LibfuzzUadpNetworkMessageDecode(ReadCapped(stream));
        }

        public static void LibfuzzUadpNetworkMessageDecode(ReadOnlySpan<byte> input)
        {
            try
            {
                _ = UadpDecoder.Decode(CopyCapped(input), NewContext());
            }
            catch (Exception ex) when (IsExpected(ex))
            {
            }
        }

        public static void AflfuzzUadpChunkReassembly(Stream stream)
        {
            LibfuzzUadpChunkReassembly(ReadCapped(stream));
        }

        public static void LibfuzzUadpChunkReassembly(ReadOnlySpan<byte> input)
        {
            try
            {
                byte[] data = CopyCapped(input);
                using var reassembler = new UadpReassembler(TimeProvider.System);
                _ = reassembler.TryAddChunk(
                    Opc.Ua.PubSub.Encoding.PublisherId.FromUInt32(0xF00DBABEU),
                    writerGroupId: 1,
                    data,
                    out ReadOnlyMemory<byte>? reassembled);
                _ = reassembled?.Length;

                if (data.Length > 0)
                {
                    var chunker = new UadpChunker();
                    int maxFrameSize = Math.Max(UadpChunker.ChunkHeaderSize + 1, Math.Min(256, data.Length + 10));
                    foreach (byte[] chunk in chunker.Split(data, messageSequenceNumber: 1, maxFrameSize))
                    {
                        _ = reassembler.TryAddChunk(
                            Opc.Ua.PubSub.Encoding.PublisherId.FromUInt32(0xF00DBABEU),
                            writerGroupId: 1,
                            chunk,
                            out reassembled);
                    }
                }
            }
            catch (Exception ex) when (IsExpected(ex))
            {
            }
        }
    }
}
