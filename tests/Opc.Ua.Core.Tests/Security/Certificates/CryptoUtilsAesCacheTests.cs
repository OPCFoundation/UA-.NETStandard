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
    public sealed class CryptoUtilsAesCacheTests
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
            using var cache = new AesCache();
            Aes? firstInstance = null;
            for (int ii = 0; ii < 3; ii++)
            {
                byte[] iv = CreateBytes(kBlockSize, (byte)(10 + ii));
                byte[] body = CreateBytes(37 + ii, (byte)(50 + ii));

                byte[] expected = EncryptWithoutCache(body, key, iv);
                byte[] actual = Encrypt(body, key, iv, cache);

                Assert.That(actual, Is.EqualTo(expected), $"message {ii}");
                Aes? instance = cache.Take();
                Assert.That(instance, Is.Not.Null);
                firstInstance ??= instance;
                Assert.That(instance, Is.SameAs(firstInstance));
                cache.Return(instance!);
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
            using var encryptCache = new AesCache();
            using var decryptCache = new AesCache();
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
                    encryptCache);

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
                    decryptCache);

                Assert.That(decrypted, Has.Count.EqualTo(kHeaderSize + bodySize));
                byte[] roundTripped = new byte[bodySize];
                Array.Copy(buffer, kHeaderSize, roundTripped, 0, bodySize);
                Assert.That(roundTripped, Is.EqualTo(body));
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
            using var cache = new AesCache();
            Parallel.For(0, 256, _ =>
            {
                byte[] actual = Encrypt(body, key, iv, cache);
                Assert.That(actual, Is.EqualTo(expected));
            });
            using Aes? instance = cache.Take();
            Assert.That(instance, Is.Not.Null);
        }

        /// <summary>
        /// Disposing a channel token disposes and clears its cached instances.
        /// </summary>
        [Test]
        public void DisposingTheTokenReleasesTheCachedInstances()
        {
            using var token = new ChannelToken();
            token.ClientAes.Return(Aes.Create());
            token.ServerAes.Return(Aes.Create());

            token.Dispose();

            using Aes? client = token.ClientAes.Take();
            using Aes? server = token.ServerAes.Take();
            Assert.That(client, Is.Null);
            Assert.That(server, Is.Null);
        }

        /// <summary>
        /// Disposal while a transform owns an instance must not retain that
        /// instance when the transform returns it to the token.
        /// </summary>
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void DisposingTheTokenDuringATransformReleasesTheInstance(bool client, bool encrypt)
        {
            byte[] key = CreateBytes(32, 8);
            byte[] iv = CreateBytes(kBlockSize, 9);
            byte[] body = CreateBytes(37, 10);
            byte[] encrypted = EncryptWithoutCache(body, key, iv);
            using var token = new ChannelToken();
            using var aes = new TransformCallbackAes(key, token.Dispose);
            AesCache cache = client ? token.ClientAes : token.ServerAes;
            cache.Return(aes);

            if (encrypt)
            {
                Assert.That(Encrypt(body, key, iv, cache), Is.EqualTo(encrypted));
            }
            else
            {
                ArraySegment<byte> decrypted = CryptoUtils.SymmetricDecryptAndVerify(
                    new ArraySegment<byte>(encrypted, kHeaderSize, encrypted.Length - kHeaderSize),
                    SecurityPolicyInfo.Basic256Sha256,
                    key,
                    iv,
                    null,
                    false,
                    0,
                    0,
                    null,
                    null,
                    cache);
                Assert.That(decrypted, Has.Count.EqualTo(kHeaderSize + body.Length));
                Assert.That(encrypted.AsSpan(kHeaderSize, body.Length).ToArray(), Is.EqualTo(body));
            }

            using Aes? clientInstance = token.ClientAes.Take();
            using Aes? serverInstance = token.ServerAes.Take();
            Assert.Multiple(() =>
            {
                Assert.That(clientInstance, Is.Null);
                Assert.That(serverInstance, Is.Null);
                Assert.That(aes.IsDisposed, Is.True);
            });
        }

        /// <summary>
        /// A return racing shutdown must release both the retained and the returned instance.
        /// </summary>
        [Test]
        public async Task ConcurrentReturnAndDisposalReleaseEveryInstanceAsync()
        {
            byte[] key = CreateBytes(32, 11);
            for (int i = 0; i < 200; i++)
            {
                using var cache = new AesCache();
                using var first = new TransformCallbackAes(key, static () => { });
                using var second = new TransformCallbackAes(key, static () => { });
                cache.Return(first);

                await Task.WhenAll(
                    Task.Run(cache.Dispose),
                    Task.Run(() => cache.Return(second))).ConfigureAwait(false);

                using Aes? instance = cache.Take();
                Assert.That(instance, Is.Null);
                Assert.That(first.IsDisposed, Is.True);
                Assert.That(second.IsDisposed, Is.True);
            }
        }

        private static byte[] Encrypt(byte[] body, byte[] key, byte[] iv, AesCache cache)
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
                cache);
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

        /// <summary>
        /// Runs a callback after the real transform has borrowed the cached AES instance.
        /// </summary>
        private sealed class TransformCallbackAes : Aes
        {
            /// <summary>
            /// Creates a CBC cipher with a callback at the transform boundary.
            /// </summary>
            public TransformCallbackAes(byte[] key, Action onTransform)
            {
                m_aes = Aes.Create();
                m_aes.Key = key;
                m_aes.Mode = CipherMode.CBC;
                m_aes.Padding = PaddingMode.None;
                Key = key;
                Padding = PaddingMode.None;
                m_onTransform = onTransform;
            }

            /// <summary>
            /// Whether the cache disposed this instance.
            /// </summary>
            public bool IsDisposed { get; private set; }

            /// <inheritdoc/>
            public override ICryptoTransform CreateEncryptor(byte[] rgbKey, byte[]? rgbIV)
            {
                m_onTransform();
#pragma warning disable CA5401 // Test-only forwarding of the protocol IV. TODO: remove when net48 is retired.
                return m_aes.CreateEncryptor(rgbKey, rgbIV);
#pragma warning restore CA5401
            }

            /// <inheritdoc/>
            public override ICryptoTransform CreateDecryptor(byte[] rgbKey, byte[]? rgbIV)
            {
                m_onTransform();
                return m_aes.CreateDecryptor(rgbKey, rgbIV);
            }

            /// <inheritdoc/>
            public override void GenerateIV()
            {
                m_aes.GenerateIV();
                IV = m_aes.IV;
            }

            /// <inheritdoc/>
            public override void GenerateKey()
            {
                m_aes.GenerateKey();
                Key = m_aes.Key;
            }

#if NET6_0_OR_GREATER
            /// <inheritdoc/>
            protected override bool TryEncryptCbcCore(
                ReadOnlySpan<byte> plaintext,
                ReadOnlySpan<byte> iv,
                Span<byte> destination,
                PaddingMode paddingMode,
                out int bytesWritten)
            {
                m_onTransform();
                return m_aes.TryEncryptCbc(plaintext, iv, destination, out bytesWritten, paddingMode);
            }

            /// <inheritdoc/>
            protected override bool TryDecryptCbcCore(
                ReadOnlySpan<byte> ciphertext,
                ReadOnlySpan<byte> iv,
                Span<byte> destination,
                PaddingMode paddingMode,
                out int bytesWritten)
            {
                m_onTransform();
                return m_aes.TryDecryptCbc(ciphertext, iv, destination, out bytesWritten, paddingMode);
            }
#endif

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    IsDisposed = true;
                    m_aes.Dispose();
                }
                base.Dispose(disposing);
            }

            private readonly Aes m_aes;
            private readonly Action m_onTransform;
        }
    }
}
