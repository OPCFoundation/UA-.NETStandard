// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using Opc.Ua.Bindings;
using Opc.Ua.Pcap.Dissection;

namespace Opc.Ua.Fuzzing
{
    public static partial class FuzzableCode
    {
        public static void AflfuzzOfflineSecureChannelReadChunk(Stream stream)
        {
            LibfuzzOfflineSecureChannelReadChunk(ReadCapped(stream));
        }

        public static void LibfuzzOfflineSecureChannelReadChunk(ReadOnlySpan<byte> input)
        {
            try
            {
                using var channel = new OfflineSecureChannel(TestKeyMaterial);
                ReadOnlySpan<byte> chunk = input;
                if (chunk.Length < TcpMessageLimits.SymmetricHeaderSize + TcpMessageLimits.SequenceHeaderSize)
                {
                    chunk = BuildChunk(TcpMessageType.Open | TcpMessageType.Final, input);
                }

                OfflineDecodedChunk decoded = channel.ReadChunk(chunk, fromClient: true);
                _ = decoded.Body.Length;
            }
            catch (Exception ex) when (IsExpected(ex))
            {
            }
        }
    }
}
