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
using Opc.Ua.Bindings;
using Opc.Ua.Core.Diagnostics.Frame;

namespace Opc.Ua.Fuzzing
{
    public static partial class FuzzableCode
    {
        public static void AflfuzzTcpStreamReassembler(Stream stream)
        {
            LibfuzzTcpStreamReassembler(ReadCapped(stream));
        }

        public static void LibfuzzTcpStreamReassembler(ReadOnlySpan<byte> input)
        {
            try
            {
                byte[] recordBytes = CopyCapped(input);
                var record = new PcapRecord(
                    DateTimeOffset.UnixEpoch,
                    PcapFileWriter.LinkTypeEthernet,
                    recordBytes.Length,
                    recordBytes);
                var reassembler = new TcpStreamReassembler();
                foreach (TcpFlowSegment segment in reassembler.Process(record))
                {
                    _ = segment.Data.Length;
                }

                byte[] loopback = LoopbackFrameBuilder.Build(true, TestChannelId, BuildChunk(TcpMessageType.Hello, input));
                var loopbackRecord = new PcapRecord(
                    DateTimeOffset.UnixEpoch,
                    PcapFileWriter.LinkTypeNull,
                    loopback.Length,
                    loopback);
                foreach (TcpFlowSegment segment in reassembler.Process(loopbackRecord))
                {
                    _ = segment.SequenceNumber;
                }
            }
            catch (Exception ex) when (IsExpected(ex))
            {
            }
        }
    }
}
