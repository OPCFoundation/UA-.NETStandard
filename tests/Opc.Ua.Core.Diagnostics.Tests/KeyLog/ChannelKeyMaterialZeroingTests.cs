// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using NUnit.Framework;
using Opc.Ua.Pcap.KeyLog;

namespace Opc.Ua.Pcap.Tests.KeyLog
{
    /// <summary>
    /// Verifies that <see cref="ChannelKeyMaterial"/> clears sensitive arrays
    /// when it is disposed.
    /// </summary>
    [TestFixture]
    public sealed class ChannelKeyMaterialZeroingTests
    {
        [Test]
        public void DisposeZeroesAllByteArrays()
        {
            ChannelKeyMaterial material = CreateMaterial();
            byte[][] buffers = GetBuffers(material);

            material.Dispose();

            foreach (byte[] buffer in buffers)
            {
                Assert.That(buffer, Is.All.Zero);
            }
        }

        [Test]
        public void DisposeIsIdempotent()
        {
            ChannelKeyMaterial material = CreateMaterial();
            material.Dispose();

            Assert.DoesNotThrow(material.Dispose);
        }

        [Test]
        public void DisposeHandlesNullArrays()
        {
            var material = new ChannelKeyMaterial(
                channelId: 1,
                tokenId: 2,
                securityPolicyUri: SecurityPolicies.Basic256Sha256,
                securityMode: MessageSecurityMode.SignAndEncrypt,
                createdAt: DateTime.UtcNow,
                lifetime: 60000,
                clientNonce: null,
                serverNonce: NonZero(1, 4),
                clientSigningKey: null,
                clientEncryptingKey: NonZero(5, 4),
                clientInitializationVector: null,
                serverSigningKey: NonZero(9, 4),
                serverEncryptingKey: null,
                serverInitializationVector: NonZero(13, 4));

            Assert.DoesNotThrow(material.Dispose);
        }

        [Test]
        public void DisposeHandlesEmptyArrays()
        {
            var material = new ChannelKeyMaterial(
                channelId: 1,
                tokenId: 2,
                securityPolicyUri: SecurityPolicies.Basic256Sha256,
                securityMode: MessageSecurityMode.SignAndEncrypt,
                createdAt: DateTime.UtcNow,
                lifetime: 60000,
                clientNonce: [],
                serverNonce: [],
                clientSigningKey: [],
                clientEncryptingKey: [],
                clientInitializationVector: [],
                serverSigningKey: [],
                serverEncryptingKey: [],
                serverInitializationVector: []);

            Assert.DoesNotThrow(material.Dispose);
        }

        private static ChannelKeyMaterial CreateMaterial()
        {
            return new ChannelKeyMaterial(
                channelId: 1,
                tokenId: 2,
                securityPolicyUri: SecurityPolicies.Basic256Sha256,
                securityMode: MessageSecurityMode.SignAndEncrypt,
                createdAt: DateTime.UtcNow,
                lifetime: 60000,
                clientNonce: NonZero(1, 4),
                serverNonce: NonZero(5, 4),
                clientSigningKey: NonZero(9, 4),
                clientEncryptingKey: NonZero(13, 4),
                clientInitializationVector: NonZero(17, 4),
                serverSigningKey: NonZero(21, 4),
                serverEncryptingKey: NonZero(25, 4),
                serverInitializationVector: NonZero(29, 4));
        }

        private static byte[][] GetBuffers(ChannelKeyMaterial material)
        {
            return
            [
                material.ClientNonce!,
                material.ServerNonce!,
                material.ClientSigningKey!,
                material.ClientEncryptingKey!,
                material.ClientInitializationVector!,
                material.ServerSigningKey!,
                material.ServerEncryptingKey!,
                material.ServerInitializationVector!
            ];
        }

        private static byte[] NonZero(byte firstValue, int length)
        {
            byte[] buffer = new byte[length];
            for (int index = 0; index < buffer.Length; index++)
            {
                buffer[index] = checked((byte)(firstValue + index));
            }

            return buffer;
        }
    }
}
