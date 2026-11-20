// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using Opc.Ua.Pcap.Frame;

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
