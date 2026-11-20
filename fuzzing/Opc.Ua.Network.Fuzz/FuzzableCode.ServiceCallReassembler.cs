// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using Opc.Ua.Bindings;
using Opc.Ua.Pcap.Dissection;

namespace Opc.Ua.Fuzzing
{
    public static partial class FuzzableCode
    {
        public static void AflfuzzServiceCallReassembler(Stream stream)
        {
            LibfuzzServiceCallReassembler(ReadCapped(stream));
        }

        public static void LibfuzzServiceCallReassembler(ReadOnlySpan<byte> input)
        {
            try
            {
                var reassembler = new ServiceCallReassembler();
                reassembler.LoadKeyMaterial(TestKeyMaterial);

                int offset = 0;
                for (int frameIndex = 0; offset < input.Length && frameIndex < 16; frameIndex++)
                {
                    int length = Math.Min(input.Length - offset, 8 + (input[offset] & 0x3F));
                    if (length == 0)
                    {
                        break;
                    }

                    ReadOnlySpan<byte> chunk = input.Slice(offset, length);
                    bool fromClient = (frameIndex & 1) == 0;
                    reassembler.Push(CreateCaptureFrame(chunk, fromClient));
                    offset += length;
                }

                if (input.IsEmpty)
                {
                    reassembler.Push(CreateCaptureFrame(BuildChunk(TcpMessageType.Hello, input), true));
                }

                foreach (DecodedServiceCall call in reassembler.DrainCompleted())
                {
                    _ = call.RequestBodySize;
                }
            }
            catch (Exception ex) when (IsExpected(ex))
            {
            }
        }
    }
}
