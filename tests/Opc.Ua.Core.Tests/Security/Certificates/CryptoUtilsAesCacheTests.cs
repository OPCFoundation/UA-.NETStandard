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
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// The keyed ciphers a channel token keeps per key and direction: the CBC
    /// transforms and the AEAD instances.
    /// </summary>
    [TestFixture]
    [Category("CryptoUtils")]
    [Parallelizable]
    [SetCulture("en-us")]
    public sealed class CryptoUtilsAesCacheTests
    {
        private const int kHeaderSize = 24;
        private const int kBlockSize = 16;

        private static readonly Dictionary<string, SecurityPolicyInfo> s_policies = new()
        {
            [nameof(SecurityPolicyInfo.Basic128Rsa15)] = SecurityPolicyInfo.Basic128Rsa15,
            [nameof(SecurityPolicyInfo.Basic256)] = SecurityPolicyInfo.Basic256,
            [nameof(SecurityPolicyInfo.Basic256Sha256)] = SecurityPolicyInfo.Basic256Sha256,
            [nameof(SecurityPolicyInfo.Aes128_Sha256_RsaOaep)] = SecurityPolicyInfo.Aes128_Sha256_RsaOaep,
            [nameof(SecurityPolicyInfo.Aes256_Sha256_RsaPss)] = SecurityPolicyInfo.Aes256_Sha256_RsaPss,
            [nameof(SecurityPolicyInfo.ECC_nistP256)] = SecurityPolicyInfo.ECC_nistP256,
            [nameof(SecurityPolicyInfo.ECC_nistP256_AesGcm)] = SecurityPolicyInfo.ECC_nistP256_AesGcm,
            [nameof(SecurityPolicyInfo.ECC_nistP256_ChaChaPoly)] = SecurityPolicyInfo.ECC_nistP256_ChaChaPoly,
            [nameof(SecurityPolicyInfo.ECC_nistP384_AesGcm)] = SecurityPolicyInfo.ECC_nistP384_AesGcm,
            [nameof(SecurityPolicyInfo.RSA_DH_AesGcm)] = SecurityPolicyInfo.RSA_DH_AesGcm,
            [nameof(SecurityPolicyInfo.RSA_DH_ChaChaPoly)] = SecurityPolicyInfo.RSA_DH_ChaChaPoly
        };

        /// <summary>
        /// The symmetric policies with a CBC cipher and an HMAC signature.
        /// </summary>
        public static IEnumerable<string> CbcPolicies
        {
            get
            {
                yield return nameof(SecurityPolicyInfo.Basic128Rsa15);
                yield return nameof(SecurityPolicyInfo.Basic256);
                yield return nameof(SecurityPolicyInfo.Basic256Sha256);
                yield return nameof(SecurityPolicyInfo.Aes128_Sha256_RsaOaep);
                yield return nameof(SecurityPolicyInfo.Aes256_Sha256_RsaPss);
            }
        }

        /// <summary>
        /// Every symmetric policy with SignAndEncrypt: CBC and the authenticated ciphers.
        /// </summary>
        public static IEnumerable<string> AllEncryptingPolicies
        {
            get
            {
                foreach (string policy in CbcPolicies)
                {
                    yield return policy;
                }
                yield return nameof(SecurityPolicyInfo.ECC_nistP256);
                yield return nameof(SecurityPolicyInfo.ECC_nistP256_AesGcm);
                yield return nameof(SecurityPolicyInfo.ECC_nistP256_ChaChaPoly);
                yield return nameof(SecurityPolicyInfo.ECC_nistP384_AesGcm);
                yield return nameof(SecurityPolicyInfo.RSA_DH_AesGcm);
                yield return nameof(SecurityPolicyInfo.RSA_DH_ChaChaPoly);
            }
        }

        /// <summary>
        /// Encrypting through a cached transform gives the same cipher text as a
        /// fresh one for every message, and the transform is reused.
        /// </summary>
        [Test]
        public void CachedTransformEncryptsLikeAFreshOne()
        {
            byte[] key = CreateBytes(32, 1);
            byte[] iv = CreateBytes(kBlockSize, 2);
            using var cache = new SymmetricCipherCache();
            CachedSymmetricCipher? firstInstance = null;
            for (int ii = 0; ii < 5; ii++)
            {
                byte[] body = CreateBytes(37 + (ii * 50), (byte)(50 + ii));

                byte[] expected = EncryptWithoutCache(body, key, iv);
                byte[] actual = Encrypt(body, key, iv, cache);

                Assert.That(actual, Is.EqualTo(expected), $"message {ii}");
                CachedSymmetricCipher? instance = cache.Take(SymmetricCipherSlot.CbcEncryptor, key, iv);
                Assert.That(instance, Is.Not.Null);
                firstInstance ??= instance;
                Assert.That(instance, Is.SameAs(firstInstance));
                cache.Return(SymmetricCipherSlot.CbcEncryptor, instance!);
            }
        }

        /// <summary>
        /// A cache asked for other key material replaces its transform rather than
        /// encrypting with the old IV or key.
        /// </summary>
        [Test]
        public void CachedTransformIsReplacedForAnotherIvOrKey()
        {
            using var cache = new SymmetricCipherCache();
            for (int ii = 0; ii < 4; ii++)
            {
                byte[] key = CreateBytes(32, (byte)(1 + (ii / 2)));
                byte[] iv = CreateBytes(kBlockSize, (byte)(10 + ii));
                byte[] body = CreateBytes(64, (byte)(50 + ii));

                Assert.That(
                    Encrypt(body, key, iv, cache),
                    Is.EqualTo(EncryptWithoutCache(body, key, iv)),
                    $"message {ii}");
            }
        }

        /// <summary>
        /// Messages signed and encrypted, then decrypted and verified through cached
        /// ciphers, round-trip for every size and give the cipher text of uncached ciphers.
        /// </summary>
        [TestCaseSource(nameof(AllEncryptingPolicies))]
        public void CachedCiphersRoundTripManyMessages(string policyName)
        {
            SecurityPolicyInfo policy = s_policies[policyName];
            AssumeSupported(policy);
            var keys = new Keys(policy, 3);
            using var encryptCache = new SymmetricCipherCache();
            using var decryptCache = new SymmetricCipherCache();
            using HMAC? signer = keys.CreateHmac(policy);
            using HMAC? verifier = keys.CreateHmac(policy);
            for (int bodySize = 0; bodySize < 300; bodySize += 7)
            {
                byte[] body = CreateBytes(bodySize, (byte)bodySize);

                byte[] expected = Seal(policy, keys, body, signer, null, (uint)bodySize);
                byte[] buffer = Seal(policy, keys, body, signer, encryptCache, (uint)bodySize);
                Assert.That(buffer, Is.EqualTo(expected), $"body {bodySize}");

                ArraySegment<byte> opened = Open(policy, keys, buffer, verifier, decryptCache, (uint)bodySize);
                Assert.That(opened, Has.Count.EqualTo(kHeaderSize + bodySize), $"body {bodySize}");
                Assert.That(buffer.AsSpan(kHeaderSize, bodySize).ToArray(), Is.EqualTo(body), $"body {bodySize}");
            }
        }

        /// <summary>
        /// A tampered signature, cipher text or tag is rejected, and the decrypting
        /// cache still opens the next intact message.
        /// </summary>
        [TestCaseSource(nameof(AllEncryptingPolicies))]
        public void TamperedMessageIsRejectedAndTheCacheRecovers(string policyName)
        {
            SecurityPolicyInfo policy = s_policies[policyName];
            AssumeSupported(policy);
            var keys = new Keys(policy, 4);
            using var decryptCache = new SymmetricCipherCache();
            using HMAC? signer = keys.CreateHmac(policy);
            using HMAC? verifier = keys.CreateHmac(policy);
            byte[] body = CreateBytes(100, 5);

            // the last byte is the signature or the tag; the first byte of the body is cipher text.
            foreach (int tamperAt in new[] { -1, kHeaderSize, 3 })
            {
                byte[] buffer = Seal(policy, keys, body, signer, null, 7);
                int index = tamperAt < 0 ? buffer.Length - 1 : tamperAt;
                buffer[index] ^= 0x01;

                Assert.That(
                    () => Open(policy, keys, buffer, verifier, decryptCache, 7),
                    Throws.InstanceOf<CryptographicException>(),
                    $"tampered at {index}");

                byte[] intact = Seal(policy, keys, body, signer, null, 8);
                Open(policy, keys, intact, verifier, decryptCache, 8);
                Assert.That(intact.AsSpan(kHeaderSize, body.Length).ToArray(), Is.EqualTo(body));
            }
        }

        /// <summary>
        /// Bad padding under a valid signature is rejected as padding, not as a
        /// signature failure, and only after the signature was checked.
        /// </summary>
        [TestCaseSource(nameof(CbcPolicies))]
        public void BadPaddingUnderAValidSignatureIsRejected(string policyName)
        {
            SecurityPolicyInfo policy = s_policies[policyName];
            var keys = new Keys(policy, 6);
            using var decryptCache = new SymmetricCipherCache();
            using HMAC signer = keys.CreateHmac(policy)!;
            using HMAC verifier = keys.CreateHmac(policy)!;

            // A plain text that ends in an inconsistent padding count, signed and
            // encrypted without the stack adding padding of its own.
            int hashLength = signer.HashSize / 8;
            int plainLength = (3 * kBlockSize) - (hashLength % kBlockSize);
            byte[] buffer = new byte[kHeaderSize + plainLength + hashLength];
            CreateBytes(plainLength, 9).CopyTo(buffer, kHeaderSize);
            buffer[kHeaderSize + plainLength - 1] = 5; // claims 5 bytes of padding that do not hold 5
            int signedLength = kHeaderSize + plainLength;
            byte[] signature = signer.ComputeHash(buffer, 0, signedLength);
            signature.CopyTo(buffer, signedLength);
            int encryptedLength = plainLength + hashLength;
            Assert.That(encryptedLength % kBlockSize, Is.Zero);
            using (var aes = Aes.Create())
            {
                aes.Padding = PaddingMode.None;
#pragma warning disable CA5401 // Test of the protocol's fixed per-token IV.
                using ICryptoTransform encryptor = aes.CreateEncryptor(keys.EncryptingKey, keys.Iv);
#pragma warning restore CA5401
                encryptor.TransformBlock(buffer, kHeaderSize, encryptedLength, buffer, kHeaderSize);
            }

            CryptographicException? ex = Assert.Throws<CryptographicException>(
                () => CryptoUtils.SymmetricDecryptAndVerify(
                    new ArraySegment<byte>(buffer, kHeaderSize, encryptedLength),
                    policy,
                    keys.EncryptingKey,
                    keys.Iv,
                    keys.SigningKey,
                    false,
                    0,
                    0,
                    verifier,
                    null,
                    decryptCache));
            Assert.That(ex!.Message, Does.Contain("padding"));
        }

        /// <summary>
        /// Concurrent users of one cache each take their own cipher while it is
        /// in use, so every result is correct.
        /// </summary>
        [TestCaseSource(nameof(AllEncryptingPolicies))]
        public void ConcurrentUsersOfOneCacheGetCorrectResults(string policyName)
        {
            SecurityPolicyInfo policy = s_policies[policyName];
            AssumeSupported(policy);
            var keys = new Keys(policy, 5);
            byte[] body = CreateBytes(300, 7);
            byte[] expected = Seal(policy, keys, body, null, null, 1);
            using var encryptCache = new SymmetricCipherCache();
            using var decryptCache = new SymmetricCipherCache();
            Parallel.For(0, 256, _ =>
            {
                byte[] actual = Seal(policy, keys, body, null, encryptCache, 1);
                Assert.That(actual, Is.EqualTo(expected));
                Open(policy, keys, actual, null, decryptCache, 1);
                Assert.That(actual.AsSpan(kHeaderSize, body.Length).ToArray(), Is.EqualTo(body));
            });
        }

        /// <summary>
        /// Disposing a channel token disposes and clears its cached ciphers.
        /// </summary>
        [Test]
        public void DisposingTheTokenReleasesTheCachedCiphers()
        {
            byte[] key = CreateBytes(32, 1);
            byte[] iv = CreateBytes(kBlockSize, 2);
            using var token = new ChannelToken();
            var client = new TrackingCipher(new DisposeCallbackTransform(static () => { }));
            var server = new TrackingCipher(new DisposeCallbackTransform(static () => { }));
            token.ClientCiphers.Return(SymmetricCipherSlot.CbcEncryptor, client.Entry(key, iv));
            token.ServerCiphers.Return(SymmetricCipherSlot.Aead, server.Entry(key, null));

            token.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(token.ClientCiphers.Take(SymmetricCipherSlot.CbcEncryptor, key, iv), Is.Null);
                Assert.That(token.ServerCiphers.Take(SymmetricCipherSlot.Aead, key, null), Is.Null);
                Assert.That(client.IsDisposed, Is.True);
                Assert.That(server.IsDisposed, Is.True);
            });
        }

        /// <summary>
        /// Disposal while a transform owns a cipher must not retain that cipher
        /// when the transform returns it to the token.
        /// </summary>
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void DisposingTheTokenDuringATransformReleasesTheCipher(bool client, bool encrypt)
        {
            byte[] key = CreateBytes(32, 8);
            byte[] iv = CreateBytes(kBlockSize, 9);
            byte[] body = CreateBytes(37, 10);
            byte[] encrypted = EncryptWithoutCache(body, key, iv);
            using var token = new ChannelToken();
            SymmetricCipherCache cache = client ? token.ClientCiphers : token.ServerCiphers;
            using var aes = Aes.Create();
            aes.Padding = PaddingMode.None;
#pragma warning disable CA5401 // Test of the protocol's fixed per-token IV.
            var transform = new DisposeCallbackTransform(
                encrypt ? aes.CreateEncryptor(key, iv) : aes.CreateDecryptor(key, iv),
                token.Dispose);
#pragma warning restore CA5401
            var tracking = new TrackingCipher(transform);
            cache.Return(
                encrypt ? SymmetricCipherSlot.CbcEncryptor : SymmetricCipherSlot.CbcDecryptor,
                tracking.Entry(key, iv));

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

            Assert.Multiple(() =>
            {
                Assert.That(token.ClientCiphers.Take(SymmetricCipherSlot.CbcEncryptor, key, iv), Is.Null);
                Assert.That(token.ClientCiphers.Take(SymmetricCipherSlot.CbcDecryptor, key, iv), Is.Null);
                Assert.That(token.ServerCiphers.Take(SymmetricCipherSlot.CbcEncryptor, key, iv), Is.Null);
                Assert.That(token.ServerCiphers.Take(SymmetricCipherSlot.CbcDecryptor, key, iv), Is.Null);
                Assert.That(tracking.IsDisposed, Is.True);
            });
        }

        /// <summary>
        /// A return racing shutdown must release both the retained and the returned cipher.
        /// </summary>
        [Test]
        public async Task ConcurrentReturnAndDisposalReleaseEveryCipherAsync()
        {
            byte[] key = CreateBytes(32, 11);
            for (int i = 0; i < 200; i++)
            {
                using var cache = new SymmetricCipherCache();
                var first = new TrackingCipher(new DisposeCallbackTransform(static () => { }));
                var second = new TrackingCipher(new DisposeCallbackTransform(static () => { }));
                cache.Return(SymmetricCipherSlot.Aead, first.Entry(key, null));

                await Task.WhenAll(
                    Task.Run(cache.Dispose),
                    Task.Run(() => cache.Return(SymmetricCipherSlot.Aead, second.Entry(key, null))))
                    .ConfigureAwait(false);

                Assert.That(cache.Take(SymmetricCipherSlot.Aead, key, null), Is.Null);
                Assert.That(first.IsDisposed, Is.True);
                Assert.That(second.IsDisposed, Is.True);
            }
        }

        private static void AssumeSupported(SecurityPolicyInfo policy)
        {
#if NET6_0_OR_GREATER
            if (policy.SymmetricEncryptionAlgorithm == SymmetricEncryptionAlgorithm.ChaCha20Poly1305)
            {
                Assume.That(ChaCha20Poly1305.IsSupported, "ChaCha20-Poly1305 is not supported here.");
            }
            if (policy.SymmetricEncryptionAlgorithm is SymmetricEncryptionAlgorithm.Aes128Gcm
                or SymmetricEncryptionAlgorithm.Aes256Gcm)
            {
                Assume.That(AesGcm.IsSupported, "AES-GCM is not supported here.");
            }
#else
            _ = policy;
#endif
        }

        /// <summary>
        /// Signs and encrypts a chunk, returning the secured buffer.
        /// </summary>
        private static byte[] Seal(
            SecurityPolicyInfo policy,
            Keys keys,
            byte[] body,
            HMAC? signer,
            SymmetricCipherCache? cache,
            uint sequenceNumber)
        {
            byte[] buffer = new byte[kHeaderSize + body.Length + 128];
            CreateBytes(kHeaderSize, 99).CopyTo(buffer, 0);
            body.CopyTo(buffer, kHeaderSize);
            using HMAC? ownSigner = signer == null ? keys.CreateHmac(policy) : null;
            ArraySegment<byte> sealedData = CryptoUtils.SymmetricEncryptAndSign(
                new ArraySegment<byte>(buffer, kHeaderSize, body.Length),
                policy,
                keys.EncryptingKey,
                keys.Iv,
                keys.SigningKey,
                signer ?? ownSigner,
                false,
                1,
                sequenceNumber,
                null,
                cache);
            return buffer.AsSpan(0, sealedData.Count).ToArray();
        }

        /// <summary>
        /// Decrypts and verifies a secured buffer in place.
        /// </summary>
        private static ArraySegment<byte> Open(
            SecurityPolicyInfo policy,
            Keys keys,
            byte[] buffer,
            HMAC? verifier,
            SymmetricCipherCache? cache,
            uint sequenceNumber)
        {
            using HMAC? ownVerifier = verifier == null ? keys.CreateHmac(policy) : null;
            return CryptoUtils.SymmetricDecryptAndVerify(
                new ArraySegment<byte>(buffer, kHeaderSize, buffer.Length - kHeaderSize),
                policy,
                keys.EncryptingKey,
                keys.Iv,
                keys.SigningKey,
                false,
                1,
                sequenceNumber,
                verifier ?? ownVerifier,
                null,
                cache);
        }

        private static byte[] Encrypt(byte[] body, byte[] key, byte[] iv, SymmetricCipherCache cache)
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
        /// The key material of one direction of a channel token for a policy.
        /// </summary>
        private sealed class Keys
        {
            public Keys(SecurityPolicyInfo policy, byte seed)
            {
                SigningKey = CreateBytes(Math.Max(policy.DerivedSignatureKeyLength, 16), seed);
                EncryptingKey = CreateBytes(policy.SymmetricEncryptionKeyLength, (byte)(seed + 1));
                Iv = CreateBytes(policy.InitializationVectorLength, (byte)(seed + 2));
            }

            public byte[] SigningKey { get; }
            public byte[] EncryptingKey { get; }
            public byte[] Iv { get; }

            public HMAC? CreateHmac(SecurityPolicyInfo policy)
            {
                return policy.CreateSignatureHmac(SigningKey);
            }
        }

        /// <summary>
        /// Records whether the cache disposed a cipher.
        /// </summary>
        private sealed class TrackingCipher
        {
            public TrackingCipher(DisposeCallbackTransform transform)
            {
                m_transform = transform;
            }

            public bool IsDisposed => m_transform.IsDisposed;

            public CachedSymmetricCipher Entry(byte[] key, byte[]? iv)
            {
                return new CachedSymmetricCipher(m_transform, key, iv);
            }

            private readonly DisposeCallbackTransform m_transform;
        }

        /// <summary>
        /// Forwards to a real transform and runs a callback when a block is transformed.
        /// </summary>
        private sealed class DisposeCallbackTransform : ICryptoTransform
        {
            public DisposeCallbackTransform(Action onTransform)
                : this(null, onTransform)
            {
            }

            public DisposeCallbackTransform(ICryptoTransform? inner, Action onTransform)
            {
                m_inner = inner;
                m_onTransform = onTransform;
            }

            public bool IsDisposed { get; private set; }

            public bool CanReuseTransform => m_inner?.CanReuseTransform ?? true;

            public bool CanTransformMultipleBlocks => m_inner?.CanTransformMultipleBlocks ?? true;

            public int InputBlockSize => m_inner?.InputBlockSize ?? kBlockSize;

            public int OutputBlockSize => m_inner?.OutputBlockSize ?? kBlockSize;

            public int TransformBlock(
                byte[] inputBuffer,
                int inputOffset,
                int inputCount,
                byte[] outputBuffer,
                int outputOffset)
            {
                m_onTransform();
                return m_inner!.TransformBlock(inputBuffer, inputOffset, inputCount, outputBuffer, outputOffset);
            }

            public byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount)
            {
                return m_inner!.TransformFinalBlock(inputBuffer, inputOffset, inputCount);
            }

            public void Dispose()
            {
                IsDisposed = true;
                m_inner?.Dispose();
            }

            private readonly ICryptoTransform? m_inner;
            private readonly Action m_onTransform;
        }
    }
}
