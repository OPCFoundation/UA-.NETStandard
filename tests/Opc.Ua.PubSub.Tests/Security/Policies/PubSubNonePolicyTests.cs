// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;

namespace Opc.Ua.PubSub.Tests.Security.Policies
{
    /// <summary>
    /// Tests for the pass-through <see cref="PubSubNonePolicy"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.4.4.3.1", Summary = "None policy")]
    public class PubSubNonePolicyTests
    {
        private static PubSubNonePolicy Policy => PubSubNonePolicy.Instance;

        [Test]
        public void Metadata_AllZero()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Policy.PolicyUri, Is.EqualTo(PubSubSecurityPolicyUri.None));
                Assert.That(Policy.SigningKeyLength, Is.Zero);
                Assert.That(Policy.EncryptingKeyLength, Is.Zero);
                Assert.That(Policy.NonceLength, Is.Zero);
                Assert.That(Policy.SignatureLength, Is.Zero);
            });
        }

        [Test]
        public void EncryptDecrypt_PassThrough()
        {
            byte[] plaintext = [1, 2, 3, 4, 5];
            byte[] ciphertext = new byte[5];
            byte[] roundTrip = new byte[5];
            Policy.Encrypt(plaintext, [], [], ciphertext);
            Assert.That(ciphertext, Is.EqualTo(plaintext));
            Policy.Decrypt(ciphertext, [], [], roundTrip);
            Assert.That(roundTrip, Is.EqualTo(plaintext));
        }

        [Test]
        public void Verify_AcceptsEmptySignature()
        {
            byte[] data = [1, 2, 3];
            Assert.That(Policy.Verify(data, [], []), Is.True);
        }

        [Test]
        public void Verify_RejectsNonEmptySignature()
        {
            byte[] data = [1, 2, 3];
            byte[] signature = new byte[1];
            Assert.That(Policy.Verify(data, signature, []), Is.False);
        }

        [Test]
        public void Sign_RejectsNonEmptyBuffer()
        {
            byte[] data = [1];
            byte[] signature = new byte[1];
            Assert.That(
                () => Policy.Sign(data, [], signature),
                Throws.ArgumentException);
        }

        [Test]
        public void Encrypt_RejectsTooShortDestination()
        {
            byte[] plaintext = new byte[5];
            byte[] ciphertext = new byte[3];
            Assert.That(
                () => Policy.Encrypt(plaintext, [], [], ciphertext),
                Throws.ArgumentException);
        }

        [Test]
        public void Decrypt_RejectsTooShortDestination()
        {
            byte[] ciphertext = new byte[5];
            byte[] plaintext = new byte[3];
            Assert.That(
                () => Policy.Decrypt(ciphertext, [], [], plaintext),
                Throws.ArgumentException);
        }
    }
}
