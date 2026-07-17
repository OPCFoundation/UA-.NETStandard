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

using NUnit.Framework;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;

namespace Opc.Ua.PubSub.Tests.Security.Policies
{
    /// <summary>
    /// Behavioural tests for <see cref="PubSubAes128CtrPolicy"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.4.4.3.1", Summary = "PubSub-Aes128-CTR algorithm bundle")]
    public class PubSubAes128CtrPolicyTests
    {
        private static PubSubAes128CtrPolicy Policy => PubSubAes128CtrPolicy.Instance;

        [Test]
        public void PolicyMetadata_MatchesSpec()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Policy.PolicyUri, Is.EqualTo(PubSubSecurityPolicyUri.PubSubAes128Ctr));
                Assert.That(Policy.SigningKeyLength, Is.EqualTo(32));
                Assert.That(Policy.EncryptingKeyLength, Is.EqualTo(16));
                Assert.That(Policy.NonceLength, Is.EqualTo(12));
                Assert.That(Policy.SignatureLength, Is.EqualTo(32));
            });
        }

        [Test]
        public void EncryptDecrypt_RoundTripsPayload()
        {
            byte[] key = new byte[16];
            byte[] nonce = new byte[12];
            byte[] plaintext = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17];
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] roundTrip = new byte[plaintext.Length];

            for (int i = 0; i < key.Length; i++)
            {
                key[i] = (byte)(i + 7);
            }
            for (int i = 0; i < nonce.Length; i++)
            {
                nonce[i] = (byte)(i * 3);
            }

            Policy.Encrypt(plaintext, key, nonce, ciphertext);
            Assert.That(ciphertext, Is.Not.EqualTo(plaintext));
            Policy.Decrypt(ciphertext, key, nonce, roundTrip);
            Assert.That(roundTrip, Is.EqualTo(plaintext));
        }

        [Test]
        public void SignVerify_RoundTripsSignature()
        {
            byte[] signingKey = new byte[32];
            byte[] data = [0x10, 0x20, 0x30, 0x40];
            byte[] signature = new byte[Policy.SignatureLength];
            for (int i = 0; i < signingKey.Length; i++)
            {
                signingKey[i] = (byte)i;
            }
            Policy.Sign(data, signingKey, signature);
            Assert.That(Policy.Verify(data, signature, signingKey), Is.True);
        }

        [Test]
        public void Verify_FailsWithWrongKey()
        {
            byte[] keyA = new byte[32];
            byte[] keyB = new byte[32];
            for (int i = 0; i < keyB.Length; i++)
            {
                keyB[i] = (byte)(i + 1);
            }
            byte[] data = [1, 2, 3];
            byte[] signature = new byte[Policy.SignatureLength];
            Policy.Sign(data, keyA, signature);
            Assert.That(Policy.Verify(data, signature, keyB), Is.False);
        }

        [Test]
        public void Verify_FailsWithTamperedData()
        {
            byte[] key = new byte[32];
            byte[] data = [1, 2, 3];
            byte[] tampered = [1, 2, 4];
            byte[] signature = new byte[Policy.SignatureLength];
            Policy.Sign(data, key, signature);
            Assert.That(Policy.Verify(tampered, signature, key), Is.False);
        }

        [Test]
        public void Verify_FailsWithWrongSignatureLength()
        {
            byte[] key = new byte[32];
            byte[] data = [1, 2, 3];
            byte[] shortSignature = new byte[16];
            Assert.That(Policy.Verify(data, shortSignature, key), Is.False);
        }

        [Test]
        public void Verify_FailsWithWrongKeyLength()
        {
            byte[] data = [1, 2, 3];
            byte[] signature = new byte[Policy.SignatureLength];
            Assert.That(Policy.Verify(data, signature, new byte[8]), Is.False);
        }

        [Test]
        public void Encrypt_RejectsTruncatedKey()
        {
            byte[] nonce = new byte[12];
            byte[] plaintext = new byte[16];
            byte[] ciphertext = new byte[16];
            Assert.That(
                () => Policy.Encrypt(plaintext, new byte[8], nonce, ciphertext),
                Throws.ArgumentException);
        }

        [Test]
        public void Sign_RejectsTruncatedKey()
        {
            byte[] data = new byte[8];
            byte[] signature = new byte[Policy.SignatureLength];
            Assert.That(
                () => Policy.Sign(data, new byte[16], signature),
                Throws.ArgumentException);
        }

        [Test]
        public void Sign_RejectsTooShortSignatureBuffer()
        {
            byte[] data = new byte[8];
            byte[] key = new byte[32];
            Assert.That(
                () => Policy.Sign(data, key, new byte[8]),
                Throws.ArgumentException);
        }
    }
}
