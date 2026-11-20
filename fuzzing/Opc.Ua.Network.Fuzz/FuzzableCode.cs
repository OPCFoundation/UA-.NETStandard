// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.KeyLog;

namespace Opc.Ua.Fuzzing
{
    public static partial class FuzzableCode
    {
        internal const int MaxFuzzInputBytes = 1024 * 1024;
        internal const uint TestChannelId = 0x12345678U;
        internal const uint TestTokenId = 0x11223344U;

        public static void FuzzInfo()
        {
            Console.WriteLine("OPC UA network-layer fuzzer for Opc.Ua.Pcap.");
            Console.WriteLine("Fuzzes TCP reassembly, UA-SC frame parsing, offline channel decode and replay seams.");
        }

        internal static ChannelKeyMaterial TestKeyMaterial { get; } = CreateTestKeyMaterial();

        internal static byte[] CopyCapped(ReadOnlySpan<byte> input)
        {
            return input.Length <= MaxFuzzInputBytes
                ? input.ToArray()
                : input[..MaxFuzzInputBytes].ToArray();
        }

        internal static byte[] ReadCapped(Stream stream)
        {
            using var memoryStream = new MemoryStream();
            byte[] buffer = new byte[4096];
            int remaining = MaxFuzzInputBytes;
            while (remaining > 0)
            {
                int read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                if (read == 0)
                {
                    break;
                }

                memoryStream.Write(buffer, 0, read);
                remaining -= read;
            }

            return memoryStream.ToArray();
        }

        internal static byte[] BuildChunk(uint messageType, ReadOnlySpan<byte> body)
        {
            int bodyLength = Math.Min(body.Length, MaxFuzzInputBytes - 8);
            byte[] chunk = new byte[8 + bodyLength];
            BinaryPrimitives.WriteUInt32LittleEndian(chunk, messageType);
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)chunk.Length);
            body[..bodyLength].CopyTo(chunk.AsSpan(8));
            return chunk;
        }

        internal static CaptureFrame CreateCaptureFrame(ReadOnlySpan<byte> chunkBytes, bool fromClient)
        {
            return new CaptureFrame(
                DateTimeOffset.UnixEpoch,
                fromClient ? CaptureFrameDirection.ClientToServer : CaptureFrameDirection.ServerToClient,
                "127.0.0.1:49152",
                "127.0.0.1:4840",
                CopyCapped(chunkBytes));
        }

        internal static bool IsExpected(Exception ex)
        {
            // IndexOutOfRangeException is intentionally NOT in this whitelist:
            // an OOB read in the binding is a real bug that fuzz should surface,
            // not swallow as an "expected" parser-rejected-input outcome
            // (security audit recommendation §7.2 #3).
            return ex is
                PcapDiagnosticsException or
                ServiceResultException or
                CryptographicException or
                IOException or
                InvalidOperationException or
                ArgumentException or
                OverflowException or
                FormatException;
        }

        private static ChannelKeyMaterial CreateTestKeyMaterial()
        {
            // Test-only key material uses SecurityPolicy None, so no secrets are embedded or generated.
            // This still drives the binding's OfflineSecureChannel path and exercises asymmetric OPN passthrough.
            return new ChannelKeyMaterial(
                TestChannelId,
                TestTokenId,
                SecurityPolicies.None,
                MessageSecurityMode.None,
                DateTime.SpecifyKind(new DateTime(2026, 1, 2, 3, 4, 5), DateTimeKind.Utc),
                60000,
                clientNonce: [0x01],
                serverNonce: [0x02],
                clientSigningKey: null,
                clientEncryptingKey: null,
                clientInitializationVector: null,
                serverSigningKey: null,
                serverEncryptingKey: null,
                serverInitializationVector: null);
        }
    }
}
