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
using NUnit.Framework;
using Opc.Ua.Pcap.KeyLog;

using Opc.Ua.Bindings;

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
