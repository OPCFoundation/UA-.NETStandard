/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
 *
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
using System.Security.Cryptography;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Regression tests for the symmetric padding handling in
    /// <see cref="CryptoUtils"/>.
    /// </summary>
    /// <remarks>
    /// Two defects are covered. The padding verification loop used to start at
    /// <c>data.Offset</c> and stop at <c>paddingSize</c>, so for every message
    /// with a header - which is every channel message - it ran zero times and no
    /// filler byte was ever checked. And the padding was removed before the
    /// signature was verified, which reports the two failures apart on a message
    /// whose padding an attacker chose: a padding oracle.
    ///
    /// OPC 10000-6 6.7.2.5.1: "The value of each byte of the padding is equal to
    /// PaddingSize", and "The signature includes the headers, all Message data,
    /// the PaddingSize and the Padding".
    /// </remarks>
    [TestFixture]
    [Category("CryptoUtils")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class CryptoUtilsSymmetricPaddingTests
    {
        /// <summary>
        /// A UA secure conversation header is 24 bytes, so the plain text never
        /// starts at offset zero on the real path.
        /// </summary>
        private const int kHeaderSize = 24;

        private const int kBlockSize = 16;

        private const int kHashSize = 32;

        /// <summary>
        /// Every body length produces a different padding length; all of them
        /// have to survive the round trip.
        /// </summary>
        [Test]
        public void SymmetricRoundTripPreservesBody([Range(0, 33)] int bodySize)
        {
            byte[] key = CreateKey(32);
            byte[] iv = CreateKey(kBlockSize);
            byte[] buffer = new byte[512];
            byte[] body = CreateKey(bodySize);

            Array.Copy(body, 0, buffer, kHeaderSize, bodySize);

            ArraySegment<byte> encrypted = CryptoUtils.SymmetricEncryptAndSign(
                new ArraySegment<byte>(buffer, kHeaderSize, bodySize),
                SecurityPolicyInfo.Basic256Sha256,
                key,
                iv);

            int encryptedLength = encrypted.Count;

            Assert.That(encryptedLength, Is.GreaterThan(kHeaderSize + bodySize));
            Assert.That((encryptedLength - kHeaderSize) % kBlockSize, Is.Zero);

            ArraySegment<byte> decrypted = CryptoUtils.SymmetricDecryptAndVerify(
                new ArraySegment<byte>(buffer, kHeaderSize, encryptedLength - kHeaderSize),
                SecurityPolicyInfo.Basic256Sha256,
                key,
                iv);

            Assert.That(decrypted, Has.Count.EqualTo(kHeaderSize + bodySize));
            Assert.That(
                new ArraySegment<byte>(buffer, kHeaderSize, bodySize).ToArray(),
                Is.EqualTo(body));
        }

        /// <summary>
        /// A padding whose count byte is right but whose filler bytes are not is
        /// rejected. Before the fix the filler was never looked at, so the body
        /// silently ended wherever the forged count byte said it did.
        /// </summary>
        [Test]
        public void SymmetricDecryptRejectsForgedPaddingFiller()
        {
            const int bodySize = 10;
            const int paddingSize = kBlockSize - ((bodySize + 1) % kBlockSize);

            byte[] key = CreateKey(32);
            byte[] iv = CreateKey(kBlockSize);
            byte[] plainText = CreatePaddedPlainText(bodySize, paddingSize);

            // the count byte still says five, but one filler byte no longer
            // repeats it.
            plainText[bodySize] = 0xAA;

            byte[] buffer = BuildMessage(plainText, key, iv);

            Assert.That(
                () => CryptoUtils.SymmetricDecryptAndVerify(
                    new ArraySegment<byte>(buffer, kHeaderSize, plainText.Length),
                    SecurityPolicyInfo.Basic256Sha256,
                    key,
                    iv),
                Throws.TypeOf<CryptographicException>());
        }

        /// <summary>
        /// The same message with intact filler is accepted, so the rejection
        /// above is the forged filler and not the shape of the message.
        /// </summary>
        [Test]
        public void SymmetricDecryptAcceptsWellFormedPadding()
        {
            const int bodySize = 10;
            const int paddingSize = kBlockSize - ((bodySize + 1) % kBlockSize);

            byte[] key = CreateKey(32);
            byte[] iv = CreateKey(kBlockSize);
            byte[] plainText = CreatePaddedPlainText(bodySize, paddingSize);
            byte[] buffer = BuildMessage(plainText, key, iv);

            ArraySegment<byte> decrypted = CryptoUtils.SymmetricDecryptAndVerify(
                new ArraySegment<byte>(buffer, kHeaderSize, plainText.Length),
                SecurityPolicyInfo.Basic256Sha256,
                key,
                iv);

            Assert.That(decrypted, Has.Count.EqualTo(kHeaderSize + bodySize));
        }

        /// <summary>
        /// A padding count that reaches past the start of the plain text is
        /// rejected rather than producing a negative body length.
        /// </summary>
        [Test]
        public void SymmetricDecryptRejectsPaddingLongerThanTheMessage()
        {
            byte[] key = CreateKey(32);
            byte[] iv = CreateKey(kBlockSize);
            byte[] plainText = new byte[kBlockSize];

            for (int ii = 0; ii < plainText.Length; ii++)
            {
                plainText[ii] = 0xFF;
            }

            byte[] buffer = BuildMessage(plainText, key, iv);

            Assert.That(
                () => CryptoUtils.SymmetricDecryptAndVerify(
                    new ArraySegment<byte>(buffer, kHeaderSize, plainText.Length),
                    SecurityPolicyInfo.Basic256Sha256,
                    key,
                    iv),
                Throws.TypeOf<CryptographicException>());
        }

        /// <summary>
        /// A tampered message fails on its signature, never on its padding. The
        /// padding of a message that did not authenticate is attacker chosen, so
        /// telling the two failures apart hands out a padding oracle.
        /// </summary>
        [Test]
        public void SymmetricDecryptReportsSignatureFailureAheadOfPadding()
        {
            const int bodySize = 10;

            byte[] key = CreateKey(32);
            byte[] iv = CreateKey(kBlockSize);
            byte[] signingKey = CreateKey(48);
            byte[] buffer = new byte[512];

            for (int ii = 0; ii < bodySize; ii++)
            {
                buffer[kHeaderSize + ii] = (byte)(ii + 1);
            }

            using HMAC hmac = SecurityPolicyInfo.Basic256Sha256.CreateSignatureHmac(signingKey)!;

            ArraySegment<byte> encrypted = CryptoUtils.SymmetricEncryptAndSign(
                new ArraySegment<byte>(buffer, kHeaderSize, bodySize),
                SecurityPolicyInfo.Basic256Sha256,
                key,
                iv,
                signingKey,
                hmac);

            int encryptedLength = encrypted.Count;

            // flip a bit in the last cipher block, which garbles the padding the
            // receiver sees as well as breaking the signature.
            buffer[encryptedLength - kHashSize - 1] ^= 0x01;

            Assert.That(
                () => CryptoUtils.SymmetricDecryptAndVerify(
                    new ArraySegment<byte>(buffer, kHeaderSize, encryptedLength - kHeaderSize),
                    SecurityPolicyInfo.Basic256Sha256,
                    key,
                    iv,
                    signingKey),
                Throws.TypeOf<CryptographicException>()
                    .With.Message.EqualTo("Invalid signature."));
        }

        /// <summary>
        /// Builds a plain text of the given body length followed by a padding
        /// laid out the way <c>AddPadding</c> lays it out.
        /// </summary>
        private static byte[] CreatePaddedPlainText(int bodySize, int paddingSize)
        {
            byte[] plainText = new byte[bodySize + paddingSize + 1];

            for (int ii = 0; ii < bodySize; ii++)
            {
                plainText[ii] = (byte)(ii + 1);
            }

            for (int ii = 0; ii < paddingSize; ii++)
            {
                plainText[bodySize + ii] = (byte)paddingSize;
            }

            plainText[bodySize + paddingSize] = (byte)paddingSize;

            return plainText;
        }

        /// <summary>
        /// Lays the plain text out behind a header and encrypts it in place the
        /// way the channel does, so the decrypt path sees exactly these bytes.
        /// </summary>
        private static byte[] BuildMessage(byte[] plainText, byte[] key, byte[] iv)
        {
            byte[] buffer = new byte[kHeaderSize + plainText.Length];

            using Aes aes = Aes.Create();

            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.Key = key;

            // CA5401: a fixed IV is the point - this has to produce exactly the
            // cipher text the decrypt path is handed, and the keys never leave
            // the test.
#pragma warning disable CA5401
            aes.IV = iv;

            using ICryptoTransform encryptor = aes.CreateEncryptor();
#pragma warning restore CA5401

            encryptor.TransformBlock(plainText, 0, plainText.Length, buffer, kHeaderSize);

            return buffer;
        }

        private static byte[] CreateKey(int length)
        {
            byte[] key = new byte[length];

            for (int ii = 0; ii < length; ii++)
            {
                key[ii] = (byte)((ii * 7) + 3);
            }

            return key;
        }
    }
}
