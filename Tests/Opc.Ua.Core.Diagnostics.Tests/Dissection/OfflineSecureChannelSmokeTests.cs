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
using System.Security.Cryptography;
using NUnit.Framework;
using Opc.Ua.Pcap.Dissection;
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Tests.Dissection
{
    [TestFixture]
    public sealed class OfflineSecureChannelSmokeTests
    {
        [TestCase(SecurityPolicies.Basic256Sha256)]
        [TestCase(SecurityPolicies.Aes256_Sha256_RsaPss)]
        [TestCase(SecurityPolicies.Aes128_Sha256_RsaOaep)]
        public void ReadChunkDecryptsStackEncryptedSymmetricChunk(string securityPolicyUri)
        {
            SecurityPolicyInfo? info = SecurityPolicies.GetInfo(securityPolicyUri);
            if (info is null)
            {
                Assert.Ignore($"Security policy is not supported on this platform: {securityPolicyUri}");
                return;
            }

            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                securityPolicyUri,
                MessageSecurityMode.SignAndEncrypt,
                channelId: 0x7777,
                tokenId: 0x8888);
            byte[] payload = RandomNumberGenerator.GetBytes(37);
            byte[] chunk = BuildEncryptedChunk(info, material, payload, sequenceNumber: 1, requestId: 2);

            using var channel = new OfflineSecureChannel(material);
            OfflineDecodedChunk decoded = channel.ReadChunk(chunk, fromClient: true);

            Assert.That(decoded.ChannelId, Is.EqualTo(material.ChannelId));
            Assert.That(decoded.TokenId, Is.EqualTo(material.TokenId));
            Assert.That(decoded.SequenceNumber, Is.EqualTo(1));
            Assert.That(decoded.RequestId, Is.EqualTo(2));
            Assert.That(decoded.Body.ToArray(), Is.EqualTo(payload).AsCollection);
        }

        private static byte[] BuildEncryptedChunk(
            SecurityPolicyInfo info,
            ChannelKeyMaterial material,
            byte[] payload,
            uint sequenceNumber,
            uint requestId)
        {
            byte[] signingKey = material.ClientSigningKey ?? throw new AssertionException("Missing signing key.");
            byte[] encryptingKey = material.ClientEncryptingKey ?? throw new AssertionException("Missing encrypting key.");
            byte[] initializationVector = material.ClientInitializationVector ??
                throw new AssertionException("Missing initialization vector.");
            int plainCount = 8 + payload.Length;
            int paddingCountSize = info.NoSymmetricEncryptionPadding ? 0 : 1;
            int count = plainCount + paddingCountSize + info.SymmetricSignatureLength;
            int padding = 0;
            if (paddingCountSize > 0)
            {
                padding = initializationVector.Length - (count % initializationVector.Length);
                if (padding == initializationVector.Length)
                {
                    padding = 0;
                }
            }

            int messageSize = 16 + plainCount + paddingCountSize + padding + info.SymmetricSignatureLength;
            byte[] buffer = new byte[messageSize];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, TcpMessageType.MessageFinal);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), (uint)messageSize);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8), material.ChannelId);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12), material.TokenId);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16), sequenceNumber);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(20), requestId);
            payload.CopyTo(buffer.AsSpan(24));

            using HMAC? hmac = info.CreateSignatureHmac(signingKey);
            ArraySegment<byte> encrypted = CryptoUtils.SymmetricEncryptAndSign(
                new ArraySegment<byte>(buffer, 16, plainCount),
                info,
                encryptingKey,
                initializationVector,
                signingKey,
                hmac,
                signOnly: false,
                material.TokenId,
                sequenceNumber);

            return [.. encrypted];
        }
    }
}
