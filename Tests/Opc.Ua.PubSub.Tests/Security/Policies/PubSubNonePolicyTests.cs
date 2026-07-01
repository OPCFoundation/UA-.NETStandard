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
            byte[] plaintext = new byte[] { 1, 2, 3, 4, 5 };
            byte[] ciphertext = new byte[5];
            byte[] roundTrip = new byte[5];
            Policy.Encrypt(plaintext, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, ciphertext);
            Assert.That(ciphertext, Is.EqualTo(plaintext));
            Policy.Decrypt(ciphertext, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, roundTrip);
            Assert.That(roundTrip, Is.EqualTo(plaintext));
        }

        [Test]
        public void Verify_AcceptsEmptySignature()
        {
            byte[] data = new byte[] { 1, 2, 3 };
            Assert.That(Policy.Verify(data, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty), Is.True);
        }

        [Test]
        public void Verify_RejectsNonEmptySignature()
        {
            byte[] data = new byte[] { 1, 2, 3 };
            byte[] signature = new byte[1];
            Assert.That(Policy.Verify(data, signature, ReadOnlySpan<byte>.Empty), Is.False);
        }

        [Test]
        public void Sign_RejectsNonEmptyBuffer()
        {
            byte[] data = new byte[] { 1 };
            byte[] signature = new byte[1];
            Assert.That(
                () => Policy.Sign(data, ReadOnlySpan<byte>.Empty, signature),
                Throws.ArgumentException);
        }

        [Test]
        public void Encrypt_RejectsTooShortDestination()
        {
            byte[] plaintext = new byte[5];
            byte[] ciphertext = new byte[3];
            Assert.That(
                () => Policy.Encrypt(plaintext, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, ciphertext),
                Throws.ArgumentException);
        }

        [Test]
        public void Decrypt_RejectsTooShortDestination()
        {
            byte[] ciphertext = new byte[5];
            byte[] plaintext = new byte[3];
            Assert.That(
                () => Policy.Decrypt(ciphertext, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, plaintext),
                Throws.ArgumentException);
        }
    }
}
