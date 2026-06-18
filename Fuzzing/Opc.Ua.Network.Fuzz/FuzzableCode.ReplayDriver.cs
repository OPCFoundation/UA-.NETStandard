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
using System.Threading.Tasks;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.Pcap.Capture.Sources;
using Opc.Ua.Bindings.Pcap.Frame;
using Opc.Ua.Bindings.Pcap.Models;
using Opc.Ua.Bindings.Pcap.Replay;

namespace Opc.Ua.Fuzzing
{
    public static partial class FuzzableCode
    {
        public static void LibfuzzMockServerReplay(ReadOnlySpan<byte> input)
        {
            try
            {
#pragma warning disable CA2000
                // Ownership of source transfers to MockServerReplay. TODO: replace once replay exposes a sync fuzz tick.
                ReplayCaptureSource source = CreateReplaySource(input, serverFrame: true);
                var replay = new MockServerReplay(source);
#pragma warning restore CA2000
                _ = replay.ListenUri;
                Complete(replay.DisposeAsync());
            }
            catch (Exception ex) when (IsExpected(ex))
            {
            }
        }

        public static void LibfuzzMockClientReplay(ReadOnlySpan<byte> input)
        {
            try
            {
#pragma warning disable CA2000
                // Ownership of source transfers to MockClientReplay. TODO: replace once replay exposes a sync fuzz tick.
                ReplayCaptureSource source = CreateReplaySource(input, serverFrame: false);
                var replay = new MockClientReplay(source, "opc.tcp://127.0.0.1:4840")
                {
#pragma warning restore CA2000
                    Speed = 1000
                };
                Complete(replay.DisposeAsync());
            }
            catch (Exception ex) when (IsExpected(ex))
            {
            }
        }

        private static ReplayCaptureSource CreateReplaySource(ReadOnlySpan<byte> input, bool serverFrame)
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "NetworkReplaySeeds");
            Directory.CreateDirectory(folder);
            string pcapPath = Path.Combine(folder, serverFrame ? "server-replay.pcap" : "client-replay.pcap");
            WriteSingleRecordPcap(pcapPath, LoopbackFrameBuilder.Build(!serverFrame, TestChannelId, BuildChunk(TcpMessageType.Hello, input)));
            var source = new ReplayCaptureSource();
            Complete(source.StartAsync(
                new StartCaptureRequest
                {
                    Source = CaptureSourceKind.Replay,
                    PcapFilePath = pcapPath
                },
                default));
            return source;
        }

        private static void Complete(ValueTask valueTask)
        {
            if (valueTask.IsCompletedSuccessfully)
            {
                valueTask.GetAwaiter().GetResult();
                return;
            }

            valueTask.AsTask().GetAwaiter().GetResult();
        }

        private static void WriteSingleRecordPcap(string path, byte[] packet)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            Span<byte> global = stackalloc byte[24];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(global, 0xA1B2C3D4U);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(global[4..], 2);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(global[6..], 4);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(global[16..], 65535U);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(global[20..], PcapFileWriter.LinkTypeNull);
            stream.Write(global);

            Span<byte> header = stackalloc byte[16];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)packet.Length);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)packet.Length);
            stream.Write(header);
            stream.Write(packet);
        }
    }
}
