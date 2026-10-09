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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// The AES instance a channel token keeps per key for the CBC policies.
    /// </summary>
    [TestFixture]
    [Category("CryptoUtils")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class CryptoUtilsAesCacheTests
    {
        private const int kHeaderSize = 24;
        private const int kBlockSize = 16;

        /// <summary>
        /// Encrypting through a cached instance gives the same cipher text as a
        /// fresh one, also when the instance is reused with another IV.
        /// </summary>
        [Test]
        public void CachedInstanceEncryptsLikeAFreshOne()
        {
            byte[] key = CreateBytes(32, 1);
            Aes? cache = null;
            try
            {
                for (int ii = 0; ii < 3; ii++)
                {
                    byte[] iv = CreateBytes(kBlockSize, (byte)(10 + ii));
                    byte[] body = CreateBytes(37 + ii, (byte)(50 + ii));

                    byte[] expected = EncryptWithoutCache(body, key, iv);
                    byte[] actual = Encrypt(body, key, iv, ref cache);

                    Assert.That(actual, Is.EqualTo(expected), $"message {ii}");
                    Assert.That(cache, Is.Not.Null);
                }
            }
            finally
            {
                cache?.Dispose();
            }
        }

        /// <summary>
        /// Messages encrypted and decrypted through cached instances round-trip.
        /// </summary>
        [Test]
        public void CachedInstancesRoundTripManyMessages()
        {
            byte[] key = CreateBytes(32, 3);
            byte[] iv = CreateBytes(kBlockSize, 4);
            Aes? encryptCache = null;
            Aes? decryptCache = null;
            try
            {
                for (int bodySize = 0; bodySize < 40; bodySize++)
                {
                    byte[] body = CreateBytes(bodySize, (byte)bodySize);
                    byte[] buffer = new byte[512];
                    Array.Copy(body, 0, buffer, kHeaderSize, bodySize);

                    ArraySegment<byte> encrypted = CryptoUtils.SymmetricEncryptAndSign(
                        new ArraySegment<byte>(buffer, kHeaderSize, bodySize),
                        SecurityPolicyInfo.Basic256Sha256,
                        key,
                        iv,
                        null,
                        null,
                        false,
                        0,
                        0,
                        null,
                        ref encryptCache);

                    ArraySegment<byte> decrypted = CryptoUtils.SymmetricDecryptAndVerify(
                        new ArraySegment<byte>(buffer, kHeaderSize, encrypted.Count - kHeaderSize),
                        SecurityPolicyInfo.Basic256Sha256,
                        key,
                        iv,
                        null,
                        false,
                        0,
                        0,
                        null,
                        null,
                        ref decryptCache);

                    Assert.That(decrypted, Has.Count.EqualTo(kHeaderSize + bodySize));
                    byte[] roundTripped = new byte[bodySize];
                    Array.Copy(buffer, kHeaderSize, roundTripped, 0, bodySize);
                    Assert.That(roundTripped, Is.EqualTo(body));
                }
            }
            finally
            {
                encryptCache?.Dispose();
                decryptCache?.Dispose();
            }
        }

        /// <summary>
        /// Concurrent users of one cache each take their own instance while it is
        /// in use, so every result is correct.
        /// </summary>
        [Test]
        public void ConcurrentUsersOfOneCacheGetCorrectResults()
        {
            byte[] key = CreateBytes(32, 5);
            byte[] iv = CreateBytes(kBlockSize, 6);
            byte[] body = CreateBytes(300, 7);
            byte[] expected = EncryptWithoutCache(body, key, iv);
            var holder = new CacheHolder();
            try
            {
                Parallel.For(0, 256, _ =>
                {
                    byte[] actual = Encrypt(body, key, iv, ref holder.Cache);
                    Assert.That(actual, Is.EqualTo(expected));
                });
                Assert.That(holder.Cache, Is.Not.Null);
            }
            finally
            {
                holder.Cache?.Dispose();
            }
        }

        /// <summary>
        /// Disposing a channel token disposes and clears its cached instances.
        /// </summary>
        [Test]
        public void DisposingTheTokenReleasesTheCachedInstances()
        {
            var token = new ChannelToken
            {
                ClientAes = Aes.Create(),
                ServerAes = Aes.Create()
            };

            token.Dispose();

            Assert.That(token.ClientAes, Is.Null);
            Assert.That(token.ServerAes, Is.Null);
        }

        private static byte[] Encrypt(byte[] body, byte[] key, byte[] iv, ref Aes? cache)
        {
            byte[] buffer = new byte[kHeaderSize + body.Length + (2 * kBlockSize)];
            Array.Copy(body, 0, buffer, kHeaderSize, body.Length);
            ArraySegment<byte> encrypted = CryptoUtils.SymmetricEncryptAndSign(
                new ArraySegment<byte>(buffer, kHeaderSize, body.Length),
                SecurityPolicyInfo.Basic256Sha256,
                key,
                iv,
                null,
                null,
                false,
                0,
                0,
                null,
                ref cache);
            byte[] result = new byte[encrypted.Count];
            Array.Copy(buffer, 0, result, 0, encrypted.Count);
            return result;
        }

        private static byte[] EncryptWithoutCache(byte[] body, byte[] key, byte[] iv)
        {
            byte[] buffer = new byte[kHeaderSize + body.Length + (2 * kBlockSize)];
            Array.Copy(body, 0, buffer, kHeaderSize, body.Length);
            ArraySegment<byte> encrypted = CryptoUtils.SymmetricEncryptAndSign(
                new ArraySegment<byte>(buffer, kHeaderSize, body.Length),
                SecurityPolicyInfo.Basic256Sha256,
                key,
                iv);
            byte[] result = new byte[encrypted.Count];
            Array.Copy(buffer, 0, result, 0, encrypted.Count);
            return result;
        }

        private static byte[] CreateBytes(int length, byte seed)
        {
            byte[] bytes = new byte[length];
            for (int ii = 0; ii < length; ii++)
            {
                bytes[ii] = unchecked((byte)((ii * 31) + seed));
            }
            return bytes;
        }

        private sealed class CacheHolder
        {
            public Aes? Cache;
        }
    }
}
