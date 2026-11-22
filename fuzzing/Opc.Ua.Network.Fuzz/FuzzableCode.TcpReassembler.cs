// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using Opc.Ua.Bindings;
using Opc.Ua.Pcap.Frame;

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
