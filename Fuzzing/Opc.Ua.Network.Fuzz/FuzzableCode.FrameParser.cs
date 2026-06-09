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
using Opc.Ua.Bindings.Pcap.Frame;

namespace Opc.Ua.Fuzzing
{
    public static partial class FuzzableCode
    {
        public static void AflfuzzOpcUaFrameParser(Stream stream)
        {
            LibfuzzOpcUaFrameParser(ReadCapped(stream));
        }

        public static void LibfuzzOpcUaFrameParser(ReadOnlySpan<byte> input)
        {
            try
            {
                var segment = new TcpFlowSegment(
                    "fuzz/0->fuzz/1",
                    "127.0.0.1:49152",
                    "127.0.0.1:4840",
                    0,
                    DateTimeOffset.UnixEpoch,
                    CopyCapped(input),
                    isFin: false,
                    isSyn: false);

                var parser = new OpcUaFrameParser();
                foreach (OpcUaChunk chunk in parser.Process(segment))
                {
                    _ = chunk.Data.Length;
                }
            }
            catch (Exception ex) when (IsExpected(ex))
            {
            }
        }
    }
}
