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

#nullable enable

using System;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Verifies the HKDF key derivation of the symmetric channel keys against OPC 10000-6 6.8.1.
    /// </summary>
    [TestFixture]
    [Category("Transport")]
    [Parallelizable(ParallelScope.All)]
    public sealed class UaSCBinaryChannelKeyDerivationTests
    {
        /// <summary>
        /// OPC 10000-6 6.8.1: when not using AuthenticatedEncryption with Sign only, the EncryptionKeyLength and
        /// InitializationVectorLength are 0 in the calculation of L, which is part of the HKDF salt.
        /// </summary>
        [TestCase(SecurityPolicies.ECC_nistP256, MessageSecurityMode.Sign)]
        [TestCase(SecurityPolicies.ECC_nistP256, MessageSecurityMode.SignAndEncrypt)]
        [TestCase(SecurityPolicies.ECC_nistP384, MessageSecurityMode.Sign)]
        [TestCase(SecurityPolicies.ECC_nistP256_AesGcm, MessageSecurityMode.Sign)]
        [TestCase(SecurityPolicies.ECC_nistP256_AesGcm, MessageSecurityMode.SignAndEncrypt)]
        public void HkdfKeyDataLengthFollowsTheSecurityMode(string policyUri, MessageSecurityMode mode)
        {
            SecurityPolicyInfo? policy = SecurityPolicies.Default.GetInfo(policyUri);
            if (policy == null)
            {
                Assert.Ignore("The policy is not supported on this platform.");
                return;
            }

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using KeyProbe client = KeyProbe.Create(policyUri, mode, telemetry);
            using KeyProbe server = KeyProbe.Create(policyUri, mode, telemetry);
            (ChannelToken clientToken, ChannelToken serverToken) = KeyProbe.Agree(client, server);
            bool signOnly = mode == MessageSecurityMode.Sign && !policy.NoSymmetricEncryptionPadding;
            int length = signOnly ? policy.DerivedSignatureKeyLength : policy.ClientKeyDataLength;

            byte[] clientKeys = Derive(policy, clientToken, "opcua-client",
                clientToken.ClientNonce!, clientToken.ServerNonce!, length);
            byte[] serverKeys = Derive(policy, clientToken, "opcua-server",
                clientToken.ServerNonce!, clientToken.ClientNonce!, length);

            AssertKeys(policy, signOnly, clientKeys,
                clientToken.ClientSigningKey, clientToken.ClientEncryptingKey, clientToken.ClientInitializationVector);
            AssertKeys(policy, signOnly, serverKeys,
                clientToken.ServerSigningKey, clientToken.ServerEncryptingKey, clientToken.ServerInitializationVector);

            Assert.That(serverToken.ClientSigningKey, Is.EqualTo(clientToken.ClientSigningKey));
            Assert.That(serverToken.ServerSigningKey, Is.EqualTo(clientToken.ServerSigningKey));

            // the derived layout must still secure and verify a message.
            BufferCollection chunks = client.WriteRequest(clientToken);
            try
            {
                Assert.That(chunks, Has.Count.EqualTo(1));
                Assert.That(() => server.ReadRequest(chunks[0]), Throws.Nothing);
            }
            finally
            {
                chunks.Release(client.Buffers, "test");
            }
        }

        private static byte[] Derive(
            SecurityPolicyInfo policy,
            ChannelToken token,
            string label,
            byte[] first,
            byte[] second,
            int length)
        {
            byte[] salt = Utils.Append(
                [(byte)(length & 0xFF), (byte)(length >> 8)],
                Encoding.UTF8.GetBytes(label),
                first,
                second);
            return Nonce.DeriveHkdfKeyData(token.Secret!, salt, policy.KeyDerivationAlgorithm, length);
        }

        private static void AssertKeys(
            SecurityPolicyInfo policy,
            bool signOnly,
            byte[] expected,
            byte[]? signingKey,
            byte[]? encryptingKey,
            byte[]? iv)
        {
            int signatureKeyLength = policy.DerivedSignatureKeyLength;
            Assert.That(signingKey, Is.EqualTo(expected.AsSpan(0, signatureKeyLength).ToArray()));
            if (signOnly)
            {
                Assert.That(encryptingKey, Is.Empty);
                Assert.That(iv, Is.Empty);
                return;
            }

            Assert.That(encryptingKey, Is.EqualTo(
                expected.AsSpan(signatureKeyLength, policy.SymmetricEncryptionKeyLength).ToArray()));
            Assert.That(iv, Is.EqualTo(
                expected.AsSpan(
                    signatureKeyLength + policy.SymmetricEncryptionKeyLength,
                    policy.InitializationVectorLength).ToArray()));
        }

        /// <summary>
        /// Exposes the protected nonce and key derivation steps of a channel.
        /// </summary>
        private sealed class KeyProbe : UaSCUaBinaryChannel
        {
            private KeyProbe(
                BufferManager buffers,
                ChannelQuotas quotas,
                MessageSecurityMode mode,
                string policyUri,
                ITelemetryContext telemetry)
                : base("key-derivation", buffers, quotas, (Certificate?)null, null, mode, policyUri, telemetry)
            {
                Buffers = buffers;
                ChannelId = 7;
            }

            public BufferManager Buffers { get; }

            public static KeyProbe Create(string policyUri, MessageSecurityMode mode, ITelemetryContext telemetry)
            {
                var context = ServiceMessageContext.Create(telemetry);
                var buffers = new BufferManager("key-derivation", 65536, telemetry);
                return new KeyProbe(buffers, new ChannelQuotas(context), mode, policyUri, telemetry);
            }

            public static (ChannelToken Client, ChannelToken Server) Agree(KeyProbe client, KeyProbe server)
            {
                byte[] clientNonce = client.CreateNonce(null)!;
                byte[] serverNonce = server.CreateNonce(null)!;
                Assert.That(client.ValidateNonce(null, serverNonce), Is.True);
                Assert.That(server.ValidateNonce(null, clientNonce), Is.True);

                // the OpenSecureChannel exchange consumed the first sequence number; the
                // AEAD policies feed the previous one into the nonce.
                Assert.That(server.VerifySequenceNumber(client.GetNewSequenceNumber(), "open"), Is.True);
                return (client.Activate(clientNonce, serverNonce), server.Activate(clientNonce, serverNonce));
            }

            public BufferCollection WriteRequest(ChannelToken token)
            {
                BufferCollection chunks = WriteSymmetricMessage(
                    TcpMessageType.Message, 5, token, new ReadRequest(), true, out bool exceeded,
                    out SendGateTicket sendTicket);

                // the probe secures the message but never writes it, so the send gate
                // has to be released here or the next write would wait on this ticket.
                ReleaseSendTicket(sendTicket);
                Assert.That(exceeded, Is.False);
                return chunks;
            }

            public void ReadRequest(ArraySegment<byte> chunk)
            {
                byte[] copy = Buffers.TakeBuffer(chunk.Count, "test");
                try
                {
                    Buffer.BlockCopy(chunk.Array!, chunk.Offset, copy, 0, chunk.Count);
                    ReadSymmetricMessage(new ArraySegment<byte>(copy, 0, chunk.Count), true,
                        out _, out uint requestId, out _);
                    Assert.That(requestId, Is.EqualTo(5));
                }
                finally
                {
                    Buffers.ReturnBuffer(copy, "test");
                }
            }

            private ChannelToken Activate(byte[] clientNonce, byte[] serverNonce)
            {
                ChannelToken token = CreateToken();
                token.TokenId = 1;
                token.ChannelId = ChannelId;
                token.ClientNonce = clientNonce;
                token.ServerNonce = serverNonce;
                ActivateToken(token);
                return token;
            }
        }
    }
}
