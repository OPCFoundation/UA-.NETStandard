// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;

namespace Opc.Ua.PubSub.Tests.Security.Policies
{
    /// <summary>
    /// Behavioural tests for <see cref="PubSubAes256CtrPolicy"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.4.4.3.1", Summary = "PubSub-Aes256-CTR algorithm bundle")]
    public class PubSubAes256CtrPolicyTests
    {
        private static PubSubAes256CtrPolicy Policy => PubSubAes256CtrPolicy.Instance;

        [Test]
        public void PolicyMetadata_MatchesSpec()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Policy.PolicyUri, Is.EqualTo(PubSubSecurityPolicyUri.PubSubAes256Ctr));
                Assert.That(Policy.SigningKeyLength, Is.EqualTo(32));
                Assert.That(Policy.EncryptingKeyLength, Is.EqualTo(32));
                Assert.That(Policy.NonceLength, Is.EqualTo(8));
                Assert.That(Policy.SignatureLength, Is.EqualTo(32));
            });
        }

        [Test]
        public void EncryptDecrypt_RoundTripsPayload()
        {
            byte[] key = new byte[32];
            byte[] nonce = new byte[12];
            byte[] plaintext = new byte[33];
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] roundTrip = new byte[plaintext.Length];

            for (int i = 0; i < key.Length; i++)
            {
                key[i] = (byte)(i + 1);
            }
            for (int i = 0; i < plaintext.Length; i++)
            {
                plaintext[i] = (byte)(i * 5);
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
            byte[] data = [9, 8, 7, 6];
            byte[] signature = new byte[Policy.SignatureLength];
            Policy.Sign(data, signingKey, signature);
            Assert.That(Policy.Verify(data, signature, signingKey), Is.True);
        }

        [Test]
        public void Verify_FailsWithWrongKey()
        {
            byte[] keyA = new byte[32];
            byte[] keyB = new byte[32];
            keyB[0] = 1;
            byte[] data = [1, 2];
            byte[] sig = new byte[Policy.SignatureLength];
            Policy.Sign(data, keyA, sig);
            Assert.That(Policy.Verify(data, sig, keyB), Is.False);
        }

        [Test]
        public void Encrypt_RejectsTruncatedKey()
        {
            byte[] nonce = new byte[12];
            byte[] plaintext = new byte[16];
            byte[] ciphertext = new byte[16];
            Assert.That(
                () => Policy.Encrypt(plaintext, new byte[16], nonce, ciphertext),
                Throws.ArgumentException);
        }

        [Test]
        public void Sign_RejectsTruncatedSigningKey()
        {
            byte[] data = new byte[1];
            byte[] sig = new byte[Policy.SignatureLength];
            Assert.That(
                () => Policy.Sign(data, new byte[Policy.SigningKeyLength - 1], sig),
                Throws.ArgumentException);
        }

        [Test]
        public void Sign_RejectsTooSmallSignatureBuffer()
        {
            byte[] data = new byte[1];
            byte[] signingKey = new byte[Policy.SigningKeyLength];
            byte[] sig = new byte[Policy.SignatureLength - 1];
            Assert.That(
                () => Policy.Sign(data, signingKey, sig),
                Throws.ArgumentException);
        }

        [Test]
        public void Verify_RejectsWrongKeyLength()
        {
            byte[] data = new byte[1];
            byte[] sig = new byte[Policy.SignatureLength];
            Assert.That(
                Policy.Verify(data, sig, new byte[Policy.SigningKeyLength - 1]),
                Is.False);
        }

        [Test]
        public void Verify_RejectsWrongSignatureLength()
        {
            byte[] data = new byte[1];
            byte[] signingKey = new byte[Policy.SigningKeyLength];
            byte[] sig = new byte[Policy.SignatureLength - 1];
            Assert.That(Policy.Verify(data, sig, signingKey), Is.False);
        }
    }
}
