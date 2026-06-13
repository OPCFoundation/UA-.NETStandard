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
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.Pcap.Bindings;
using Opc.Ua.Bindings.Pcap.Frame;
using Opc.Ua.Bindings.Pcap.KeyLog;

namespace Opc.Ua.Fuzzing
{
    public static partial class Testcases
    {
        public static void Run(string workPath, ITelemetryContext telemetry)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workPath);
            _ = telemetry;
            Directory.CreateDirectory(workPath);
            string tcpPath = Path.Combine(workPath + ".Tcp");
            string chunksPath = Path.Combine(workPath + ".Chunks");
            string keysPath = Path.Combine(workPath + ".Keys");
            Directory.CreateDirectory(tcpPath);
            Directory.CreateDirectory(chunksPath);
            Directory.CreateDirectory(keysPath);

            byte[][] chunks =
            [
                FuzzableCode.BuildChunk(TcpMessageType.Hello, CreateHelloBody()),
                FuzzableCode.BuildChunk(TcpMessageType.Acknowledge, CreateAcknowledgeBody()),
                FuzzableCode.BuildChunk(TcpMessageType.Open | TcpMessageType.Final, CreateOpenBody()),
                FuzzableCode.BuildChunk(TcpMessageType.MessageFinal, CreateSymmetricBody(1, 1)),
                FuzzableCode.BuildChunk(TcpMessageType.Close | TcpMessageType.Final, CreateSymmetricBody(2, 2))
            ];

            for (int ii = 0; ii < chunks.Length; ii++)
            {
                File.WriteAllBytes(Path.Combine(chunksPath, $"chunk-{ii:000}.bin"), chunks[ii]);
                byte[] frame = LoopbackFrameBuilder.Build((ii & 1) == 0, FuzzableCode.TestChannelId, chunks[ii]);
                File.WriteAllBytes(Path.Combine(tcpPath, $"segment-{ii:000}.bin"), frame);
            }

            WriteKeyLogAsync(Path.Combine(keysPath, "token-000.json")).GetAwaiter().GetResult();
            WriteTransportTestcases(workPath, telemetry);

            // The full in-process client/server recorder requires more setup than this fleet slice.
            // These deterministic seeds are built with LoopbackFrameBuilder and exercise the same public pcap surfaces.
            _ = typeof(CapturingByteTransportFactory);
        }

        private static async Task WriteKeyLogAsync(string path)
        {
            var writer = new UaKeyLogJsonWriter(path);
            try
            {
                await writer.AppendAsync(FuzzableCode.TestKeyMaterial, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static byte[] CreateHelloBody()
        {
            using var stream = new MemoryStream();
            WriteUInt32(stream, 0);
            WriteUInt32(stream, 65536);
            WriteUInt32(stream, 65536);
            WriteUInt32(stream, 8192);
            WriteUInt32(stream, 8192);
            WriteUInt32(stream, 600000);
            WriteString(stream, "opc.tcp://localhost:4840");
            return stream.ToArray();
        }

        private static byte[] CreateAcknowledgeBody()
        {
            byte[] body = new byte[20];
            BinaryPrimitives.WriteUInt32LittleEndian(body, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), 65536);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8), 65536);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(12), 8192);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(16), 8192);
            return body;
        }

        private static byte[] CreateOpenBody()
        {
            using var stream = new MemoryStream();
            WriteUInt32(stream, FuzzableCode.TestChannelId);
            WriteUInt32(stream, 0);
            WriteString(stream, SecurityPolicies.None);
            WriteByteString(stream, []);
            WriteByteString(stream, []);
            WriteUInt32(stream, 1);
            WriteUInt32(stream, 1);
            return stream.ToArray();
        }

        private static byte[] CreateSymmetricBody(uint sequenceNumber, uint requestId)
        {
            byte[] body = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(body, FuzzableCode.TestChannelId);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), FuzzableCode.TestTokenId);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8), sequenceNumber);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(12), requestId);
            return body;
        }

        private static void WriteString(MemoryStream stream, string value)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
            WriteUInt32(stream, (uint)bytes.Length);
            stream.Write(bytes);
        }

        private static void WriteByteString(MemoryStream stream, ReadOnlySpan<byte> value)
        {
            WriteUInt32(stream, (uint)value.Length);
            stream.Write(value);
        }

        private static void WriteUInt32(MemoryStream stream, uint value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
            stream.Write(buffer);
        }
    }
}
