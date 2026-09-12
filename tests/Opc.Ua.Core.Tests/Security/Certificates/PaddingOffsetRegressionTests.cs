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
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    [TestFixture]
    [Category("CryptoUtils")]
    public sealed class PaddingOffsetRegressionTests
    {
        [TestCase(0)]
        [TestCase(8)]
        [TestCase(24)]
        public void SignedEncryptedMessagesRejectEveryCorruptedPaddingByte(int offset)
        {
            const int dataLength = 18;
            const int paddingLength = 13;
            SecurityPolicyInfo policy = SecurityPolicies.Default.GetInfo(SecurityPolicies.Basic256Sha256);
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.GenerateKey();
            aes.GenerateIV();
            using var hmac = new HMACSHA256();
            for (int corrupt = -1; corrupt < paddingLength; corrupt++)
            {
                byte[] buffer = new byte[offset + 64];
                buffer.AsSpan(0, offset).Fill(0x44);
                buffer.AsSpan(offset, dataLength).Fill(0x31);
                buffer.AsSpan(offset + dataLength, paddingLength + 1).Fill(paddingLength);
                if (corrupt >= 0)
                {
                    buffer[offset + dataLength + corrupt] ^= 1;
                }
                byte[] signature = hmac.ComputeHash(buffer, 0, offset + 32);
                signature.CopyTo(buffer, offset + 32);
                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    byte[] encrypted = encryptor.TransformFinalBlock(buffer, offset, 64);
                    encrypted.CopyTo(buffer, offset);
                }
                var message = new ArraySegment<byte>(buffer, offset, 64);
                if (corrupt < 0)
                {
                    ArraySegment<byte> plaintext = CryptoUtils.SymmetricDecryptAndVerify(
                        message, policy, aes.Key, aes.IV, hmac.Key, false, 1, 1);
                    Assert.That(plaintext, Has.Count.EqualTo(offset + dataLength));
                    Assert.That(plaintext.Array.AsSpan(offset, dataLength).ToArray(), Is.All.EqualTo(0x31));
                }
                else
                {
                    Assert.Throws<CryptographicException>(() => CryptoUtils.SymmetricDecryptAndVerify(
                        message, policy, aes.Key, aes.IV, hmac.Key, false, 1, 1), $"padding byte {corrupt}");
                }
            }
        }

        [TestCase(16, 0)]
        [TestCase(16, 24)]
        [TestCase(512, 0)]
        [TestCase(512, 24)]
        public void PaddingHelperPreservesOffsetAndBothLengthBytes(int blockSize, int offset)
        {
            var add = Bind<Func<ArraySegment<byte>, int, int, ArraySegment<byte>>>("AddPadding");
            var remove = Bind<Func<ArraySegment<byte>, int, ArraySegment<byte>>>("RemovePadding");
            byte[] bytes = new byte[offset + blockSize * 2];
            bytes.AsSpan(0, offset + 2).Fill(0x31);
            ArraySegment<byte> padded = add(new ArraySegment<byte>(bytes, offset, 2), blockSize, 0);
            ArraySegment<byte> unpadded = remove(padded, blockSize);
            Assert.That(unpadded.Offset, Is.Zero);
            Assert.That(unpadded, Has.Count.EqualTo(offset + 2));
            Assert.That(unpadded.AsSpan().ToArray(), Is.All.EqualTo(0x31));
        }

        [TestCase(0, 16)]
        [TestCase(0, 512)]
        [TestCase(1, 512)]
        public void TruncatedPaddingLengthIsAProtocolError(int count, int blockSize)
        {
            var remove = Bind<Func<ArraySegment<byte>, int, ArraySegment<byte>>>("RemovePadding");
            Assert.Throws<CryptographicException>(() => remove(new ArraySegment<byte>(new byte[count]), blockSize));
        }

        private static T Bind<T>(string methodName) where T : Delegate
        {
            MethodInfo method = typeof(CryptoUtils).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
#if NET5_0_OR_GREATER
            return method.CreateDelegate<T>();
#else
            return (T)method.CreateDelegate(typeof(T));
#endif
        }
    }
}
