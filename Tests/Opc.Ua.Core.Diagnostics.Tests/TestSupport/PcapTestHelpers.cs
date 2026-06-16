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
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Core.Diagnostics.Frame;
using Opc.Ua.Core.Diagnostics.KeyLog;

using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Diagnostics.Tests
{
    public abstract class TempDirectoryFixture
    {
        [SetUp]
        public void SetUpTempDirectory()
        {
            string path = Path.GetTempFileName();
            File.Delete(path);
            Directory.CreateDirectory(path);
            TempDirectory = path;
        }

        [TearDown]
        public void TearDownTempDirectory()
        {
            if (Directory.Exists(TempDirectory))
            {
                Directory.Delete(TempDirectory, recursive: true);
            }
        }

        protected string TempDirectory { get; private set; } = string.Empty;

        protected string CreateTempPath(string fileName)
        {
            return Path.Combine(TempDirectory, fileName);
        }
    }

    internal static class PcapTestHelpers
    {
        public static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> values)
        {
            var list = new List<T>();
            await foreach (T value in values.WithCancellation(CancellationToken.None).ConfigureAwait(false))
            {
                list.Add(value);
            }

            return list;
        }

        public static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> values, int maxCount)
        {
            var list = new List<T>();
            await foreach (T value in values.WithCancellation(CancellationToken.None).ConfigureAwait(false))
            {
                list.Add(value);
                if (list.Count >= maxCount)
                {
                    break;
                }
            }

            return list;
        }

        public static SecurityPolicyInfo GetPolicy(string uri)
        {
            return SecurityPolicies.GetInfo(uri) ?? throw new AssertionException($"Policy is not available: {uri}");
        }

        public static ChannelKeyMaterial CreateMaterial(
            string securityPolicyUri,
            MessageSecurityMode mode,
            uint channelId = 0x12345678,
            uint tokenId = 0x11223344)
        {
            SecurityPolicyInfo info = GetPolicy(securityPolicyUri);
            byte[]? signingKey = mode == MessageSecurityMode.None || info.DerivedSignatureKeyLength == 0
                ? null
                : RandomNumberGenerator.GetBytes(info.DerivedSignatureKeyLength);
            byte[]? encryptingKey = mode != MessageSecurityMode.SignAndEncrypt
                ? null
                : RandomNumberGenerator.GetBytes(info.SymmetricEncryptionKeyLength);
            byte[]? initializationVector = mode != MessageSecurityMode.SignAndEncrypt
                ? null
                : RandomNumberGenerator.GetBytes(info.InitializationVectorLength);

            return new ChannelKeyMaterial(
                channelId,
                tokenId,
                securityPolicyUri,
                mode,
                DateTime.SpecifyKind(new DateTime(2026, 1, 2, 3, 4, 5), DateTimeKind.Utc),
                60000,
                RandomNumberGenerator.GetBytes(Math.Max(1, info.SecureChannelNonceLength)),
                RandomNumberGenerator.GetBytes(Math.Max(1, info.SecureChannelNonceLength)),
                signingKey,
                encryptingKey,
                initializationVector,
                signingKey is null ? null : RandomNumberGenerator.GetBytes(signingKey.Length),
                encryptingKey is null ? null : RandomNumberGenerator.GetBytes(encryptingKey.Length),
                initializationVector is null ? null : RandomNumberGenerator.GetBytes(initializationVector.Length));
        }

        public static void AssertMaterialEqual(
            ChannelKeyMaterial actual,
            ChannelKeyMaterial expected,
            bool includeJsonOnlyFields)
        {
            Assert.That(actual.ChannelId, Is.EqualTo(expected.ChannelId));
            Assert.That(actual.TokenId, Is.EqualTo(expected.TokenId));
            Assert.That(actual.SecurityPolicyUri, Is.EqualTo(expected.SecurityPolicyUri));
            Assert.That(actual.SecurityMode, Is.EqualTo(expected.SecurityMode));
            AssertBytes(actual.ClientSigningKey, expected.ClientSigningKey);
            AssertBytes(actual.ClientEncryptingKey, expected.ClientEncryptingKey);
            AssertBytes(actual.ClientInitializationVector, expected.ClientInitializationVector);
            AssertBytes(actual.ServerSigningKey, expected.ServerSigningKey);
            AssertBytes(actual.ServerEncryptingKey, expected.ServerEncryptingKey);
            AssertBytes(actual.ServerInitializationVector, expected.ServerInitializationVector);

            if (includeJsonOnlyFields)
            {
                Assert.That(actual.CreatedAt, Is.EqualTo(expected.CreatedAt));
                Assert.That(actual.Lifetime, Is.EqualTo(expected.Lifetime));
                AssertBytes(actual.ClientNonce, expected.ClientNonce);
                AssertBytes(actual.ServerNonce, expected.ServerNonce);
            }
        }

        public static void AssertBytes(byte[]? actual, byte[]? expected)
        {
            if (expected is null)
            {
                Assert.That(actual, Is.Null);
                return;
            }

            Assert.That(actual, Is.EqualTo(expected).AsCollection);
        }

        public static byte[] BuildOpcUaChunk(uint messageType, int bodyLength, byte fill = 0x5A)
        {
            byte[] chunk = new byte[8 + bodyLength];
            BinaryPrimitives.WriteUInt32LittleEndian(chunk, messageType);
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)chunk.Length);
            chunk.AsSpan(8).Fill(fill);
            return chunk;
        }

        public static TcpFlowSegment CreateSegment(string flowKey, byte[] data)
        {
            return new TcpFlowSegment(
                flowKey,
                "127.0.0.1:50000",
                "127.0.0.1:4840",
                1,
                DateTimeOffset.UtcNow,
                data,
                isFin: false,
                isSyn: false);
        }

        public static byte[] BuildEthernetTcpPacket(
            byte[] payload,
            uint sequenceNumber = 1,
            ushort sourcePort = 50000,
            ushort destinationPort = 4840,
            bool fin = false)
        {
            byte[] packet = new byte[14 + 20 + 20 + payload.Length];
            packet.AsSpan(0, 6).Fill(0x11);
            packet.AsSpan(6, 6).Fill(0x22);
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(12), 0x0800);
            WriteIpv4Tcp(packet.AsSpan(14), payload, sequenceNumber, sourcePort, destinationPort, fin);
            return packet;
        }

        public static byte[] BuildRawIpv4TcpPacket(
            byte[] payload,
            uint sequenceNumber = 1,
            ushort sourcePort = 50000,
            ushort destinationPort = 4840,
            bool fin = false)
        {
            byte[] packet = new byte[20 + 20 + payload.Length];
            WriteIpv4Tcp(packet, payload, sequenceNumber, sourcePort, destinationPort, fin);
            return packet;
        }

        public static ushort FoldOnesComplement(uint sum)
        {
            while ((sum >> 16) != 0)
            {
                sum = (sum & 0xFFFFU) + (sum >> 16);
            }

            return (ushort)sum;
        }

        public static uint SumWords(ReadOnlySpan<byte> data)
        {
            uint sum = 0;
            int index = 0;
            while (index + 1 < data.Length)
            {
                sum += BinaryPrimitives.ReadUInt16BigEndian(data[index..]);
                index += 2;
            }

            if (index < data.Length)
            {
                sum += (uint)(data[index] << 8);
            }

            return sum;
        }

        private static void WriteIpv4Tcp(
            Span<byte> packet,
            byte[] payload,
            uint sequenceNumber,
            ushort sourcePort,
            ushort destinationPort,
            bool fin)
        {
            Span<byte> ip = packet[..20];
            ip[0] = 0x45;
            BinaryPrimitives.WriteUInt16BigEndian(ip[2..], (ushort)(40 + payload.Length));
            BinaryPrimitives.WriteUInt16BigEndian(ip[6..], 0x4000);
            ip[8] = 64;
            ip[9] = 6;
            IPAddress.Parse("10.0.0.1").GetAddressBytes().CopyTo(ip[12..16]);
            IPAddress.Parse("10.0.0.2").GetAddressBytes().CopyTo(ip[16..20]);
            BinaryPrimitives.WriteUInt16BigEndian(ip[10..], (ushort)~FoldOnesComplement(SumWords(ip)));

            Span<byte> tcp = packet.Slice(20, 20);
            BinaryPrimitives.WriteUInt16BigEndian(tcp, sourcePort);
            BinaryPrimitives.WriteUInt16BigEndian(tcp[2..], destinationPort);
            BinaryPrimitives.WriteUInt32BigEndian(tcp[4..], sequenceNumber);
            tcp[12] = 0x50;
            tcp[13] = (byte)(0x18 | (fin ? 0x01 : 0x00));
            BinaryPrimitives.WriteUInt16BigEndian(tcp[14..], 0xFFFF);
            payload.CopyTo(packet[40..]);
        }
    }
}
