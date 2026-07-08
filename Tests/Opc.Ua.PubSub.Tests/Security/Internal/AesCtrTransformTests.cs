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
using Opc.Ua.PubSub.Security.Internal;

namespace Opc.Ua.PubSub.Tests.Security.Internal
{
    /// <summary>
    /// Exercises the manual AES-CTR keystream implementation against
    /// the canonical NIST SP 800-38A test vectors and against
    /// argument-validation paths.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.4.4.3.1", Summary = "AES-CTR known-answer test from NIST SP 800-38A F.5.1")]
    public class AesCtrTransformTests
    {
        /// <summary>
        /// NIST SP 800-38A appendix F.5.1 (CTR-AES128.Encrypt).
        /// </summary>
        private static readonly byte[] s_key128 = HexToBytes(
            "2b7e151628aed2a6abf7158809cf4f3c");
        private static readonly byte[] s_initialCounter = HexToBytes(
            "f0f1f2f3f4f5f6f7f8f9fafbfcfdfeff");
        private static readonly byte[] s_plaintext = HexToBytes(
            "6bc1bee22e409f96e93d7e117393172a"
            + "ae2d8a571e03ac9c9eb76fac45af8e51"
            + "30c81c46a35ce411e5fbc1191a0a52ef"
            + "f69f2445df4f9b17ad2b417be66c3710");
        private static readonly byte[] s_ciphertext = HexToBytes(
            "874d6191b620e3261bef6864990db6ce"
            + "9806f66b7970fdff8617187bb9fffdff"
            + "5ae4df3edbd5d35e5b4f09020db03eab"
            + "1e031dda2fbe03d1792170a0f3009cee");

        [Test]
        [TestSpec("7.2.4.4.3.1", Summary = "NIST F.5.1 AES-128-CTR encrypt round-trip")]
        public void EncryptOrDecryptWithCounter_AgainstNistVector_ProducesExpectedCiphertext()
        {
            byte[] output = new byte[s_plaintext.Length];
            AesCtrTransform.EncryptOrDecryptWithCounter(
                s_key128,
                s_initialCounter,
                s_plaintext,
                output);
            Assert.That(output, Is.EqualTo(s_ciphertext));
        }

        [Test]
        [TestSpec("7.2.4.4.3.1", Summary = "AES-CTR is symmetric (decrypt == encrypt)")]
        public void EncryptOrDecryptWithCounter_IsSymmetric()
        {
            byte[] roundTrip = new byte[s_plaintext.Length];
            AesCtrTransform.EncryptOrDecryptWithCounter(
                s_key128,
                s_initialCounter,
                s_ciphertext,
                roundTrip);
            Assert.That(roundTrip, Is.EqualTo(s_plaintext));
        }

        [Test]
        [TestSpec("7.2.4.4.3.1", Summary = "Partial-block input is handled without padding")]
        public void EncryptOrDecrypt_HandlesPartialBlockInput()
        {
            byte[] nonce = new byte[12];
            byte[] key = new byte[16];
            byte[] plaintext = new byte[7] { 1, 2, 3, 4, 5, 6, 7 };
            byte[] ciphertext = new byte[7];
            byte[] roundTrip = new byte[7];
            AesCtrTransform.EncryptOrDecrypt(key, nonce, plaintext, ciphertext);
            AesCtrTransform.EncryptOrDecrypt(key, nonce, ciphertext, roundTrip);
            Assert.That(roundTrip, Is.EqualTo(plaintext));
        }

        [Test]
        public void EncryptOrDecrypt_RejectsWrongKeyLength()
        {
            byte[] nonce = new byte[12];
            byte[] input = new byte[16];
            byte[] output = new byte[16];
            Assert.That(
                () => AesCtrTransform.EncryptOrDecrypt(new byte[7], nonce, input, output),
                Throws.ArgumentException);
        }

        [Test]
        public void EncryptOrDecrypt_RejectsWrongNonceLength()
        {
            byte[] key = new byte[16];
            byte[] input = new byte[16];
            byte[] output = new byte[16];
            Assert.That(
                () => AesCtrTransform.EncryptOrDecrypt(key, new byte[8], input, output),
                Throws.ArgumentException);
        }

        [Test]
        public void EncryptOrDecrypt_RejectsTooShortOutput()
        {
            byte[] key = new byte[16];
            byte[] nonce = new byte[12];
            byte[] input = new byte[16];
            byte[] output = new byte[8];
            Assert.That(
                () => AesCtrTransform.EncryptOrDecrypt(key, nonce, input, output),
                Throws.ArgumentException);
        }

        [Test]
        public void EncryptOrDecryptWithCounter_RejectsWrongCounterLength()
        {
            byte[] key = new byte[16];
            byte[] input = new byte[16];
            byte[] output = new byte[16];
            Assert.That(
                () => AesCtrTransform.EncryptOrDecryptWithCounter(
                    key,
                    new byte[8],
                    input,
                    output),
                Throws.ArgumentException);
            Assert.That(
                () => AesCtrTransform.EncryptOrDecryptWithCounter(
                    key,
                    new byte[16],
                    input,
                    new byte[4]),
                Throws.ArgumentException);
        }

        [Test]
        public void EncryptOrDecryptWithStartingBlock_AdvancesCounter()
        {
            byte[] key = new byte[16];
            byte[] nonce = new byte[12];
            byte[] plaintext = new byte[16];
            byte[] block0 = new byte[16];
            byte[] block1 = new byte[16];
            AesCtrTransform.EncryptOrDecrypt(key, nonce, plaintext, block0);
            // Block 1 keystream differs from block 0.
            AesCtrTransform.EncryptOrDecryptWithStartingBlock(
                key,
                nonce,
                1,
                plaintext,
                block1);
            Assert.That(block1, Is.Not.EqualTo(block0));
        }

        private static byte[] HexToBytes(string hex)
        {
            if (hex.Length % 2 != 0)
            {
                throw new ArgumentException("Hex length must be even.", nameof(hex));
            }
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }
    }
}
