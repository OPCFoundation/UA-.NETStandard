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
using NUnit.Framework;
#if NETFRAMEWORK || AEAD_POLYFILL_CROSSCHECK
using PolyfillAesGcm = Opc.Ua.Security.Certificates.BouncyCastle.AesGcm;
using PolyfillChaCha20Poly1305 = Opc.Ua.Security.Certificates.BouncyCastle.ChaCha20Poly1305;
#endif

namespace Opc.Ua.Core.Tests.Security.Crypto
{
    /// <summary>
    /// Known answers for the authenticated ciphers of the AEAD security
    /// policies. On .NET Framework they run in the BouncyCastle polyfill, on
    /// .NET 8+ in the BCL; the polyfill is also cross-checked against the BCL
    /// where both are compiled in.
    /// </summary>
    [TestFixture]
    [Category("CryptoProvider")]
    [Parallelizable(ParallelScope.All)]
    [SetCulture("en-us")]
    public sealed class AeadPolyfillTests
    {
        public sealed record KnownAnswer(
            string Name,
            SymmetricEncryptionAlgorithm Algorithm,
            string Key,
            string Nonce,
            string AssociatedData,
            string Plaintext,
            string Ciphertext,
            string Tag)
        {
            public override string ToString()
            {
                return Name;
            }
        }

        // McGrew and Viega, "The Galois/Counter Mode of Operation", the test
        // cases NIST publishes with SP 800-38D, and RFC 8439 section 2.8.2.
        private const string kGcmPlaintext =
            "d9313225f88406e5a55909c5aff5269a86a7a9531534f7da2e4c303d8a318a72" +
            "1c3c0c95956809532fcf0e2449a6b525b16aedf5aa0de657ba637b39";

        private const string kGcmAssociatedData = "feedfacedeadbeeffeedfacedeadbeefabaddad2";

        public static IEnumerable<KnownAnswer> KnownAnswers()
        {
            yield return new KnownAnswer(
                "AES-128-GCM test case 2",
                SymmetricEncryptionAlgorithm.Aes128Gcm,
                "00000000000000000000000000000000",
                "000000000000000000000000",
                string.Empty,
                "00000000000000000000000000000000",
                "0388dace60b6a392f328c2b971b2fe78",
                "ab6e47d42cec13bdf53a67b21257bddf");
            yield return new KnownAnswer(
                "AES-128-GCM test case 4",
                SymmetricEncryptionAlgorithm.Aes128Gcm,
                "feffe9928665731c6d6a8f9467308308",
                "cafebabefacedbaddecaf888",
                kGcmAssociatedData,
                kGcmPlaintext,
                "42831ec2217774244b7221b784d0d49ce3aa212f2c02a4e035c17e2329aca12e" +
                "21d514b25466931c7d8f6a5aac84aa051ba30b396a0aac973d58e091",
                "5bc94fbc3221a5db94fae95ae7121a47");
            yield return new KnownAnswer(
                "AES-256-GCM test case 14",
                SymmetricEncryptionAlgorithm.Aes256Gcm,
                "0000000000000000000000000000000000000000000000000000000000000000",
                "000000000000000000000000",
                string.Empty,
                "00000000000000000000000000000000",
                "cea7403d4d606b6e074ec5d3baf39d18",
                "d0d1c8a799996bf0265b98b5d48ab919");
            yield return new KnownAnswer(
                "AES-256-GCM test case 16",
                SymmetricEncryptionAlgorithm.Aes256Gcm,
                "feffe9928665731c6d6a8f9467308308feffe9928665731c6d6a8f9467308308",
                "cafebabefacedbaddecaf888",
                kGcmAssociatedData,
                kGcmPlaintext,
                "522dc1f099567d07f47f37a32a84427d643a8cdcbfe5c0c97598a2bd2555d1aa" +
                "8cb08e48590dbb3da7b08b1056828838c5f61e6393ba7a0abcc9f662",
                "76fc6ece0f4e1768cddf8853bb2d551b");
            yield return new KnownAnswer(
                "ChaCha20-Poly1305 RFC 8439 2.8.2",
                SymmetricEncryptionAlgorithm.ChaCha20Poly1305,
                "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f",
                "070000004041424344454647",
                "50515253c0c1c2c3c4c5c6c7",
                // "Ladies and Gentlemen of the class of '99: If I could offer you
                // only one tip for the future, sunscreen would be it."
                "4c616469657320616e642047656e746c656d656e206f662074686520636c6173" +
                "73206f66202739393a204966204920636f756c64206f6666657220796f75206f" +
                "6e6c79206f6e652074697020666f7220746865206675747572652c2073756e73" +
                "637265656e20776f756c642062652069742e",
                "d31a8d34648e60db7b86afbc53ef7ec2a4aded51296e08fea9e2b5a736ee62d6" +
                "3dbea45e8ca9671282fafb69da92728b1a71de0a9e060b2905d6a5b67ecd3b36" +
                "92ddbd7f2d778b8c9803aee328091b58fab324e4fad675945585808b4831d7bc" +
                "3ff4def08e4b7a9de576d26586cec64b6116",
                "1ae10b594f09e26a7e902ecbd0600691");
        }

        [TestCaseSource(nameof(KnownAnswers))]
        public void PlatformProviderMatchesTheKnownAnswer(KnownAnswer vector)
        {
            PlatformSymmetricCryptoProvider provider = PlatformSymmetricCryptoProvider.Instance;
            Assert.That(provider.Supports(vector.Algorithm), Is.True);

            byte[] plaintext = CoreUtils.FromHexString(vector.Plaintext);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];
            byte[] recovered = new byte[plaintext.Length];

            provider.EncryptAuthenticated(
                vector.Algorithm,
                CoreUtils.FromHexString(vector.Key),
                CoreUtils.FromHexString(vector.Nonce),
                plaintext,
                ciphertext,
                tag,
                CoreUtils.FromHexString(vector.AssociatedData));

            bool verified = provider.DecryptAuthenticated(
                vector.Algorithm,
                CoreUtils.FromHexString(vector.Key),
                CoreUtils.FromHexString(vector.Nonce),
                CoreUtils.FromHexString(vector.Ciphertext),
                CoreUtils.FromHexString(vector.Tag),
                recovered,
                CoreUtils.FromHexString(vector.AssociatedData));

            Assert.Multiple(() =>
            {
                Assert.That(ciphertext, Is.EqualTo(CoreUtils.FromHexString(vector.Ciphertext)));
                Assert.That(tag, Is.EqualTo(CoreUtils.FromHexString(vector.Tag)));
                Assert.That(verified, Is.True);
                Assert.That(recovered, Is.EqualTo(plaintext));
            });
        }

        [TestCaseSource(nameof(TamperedKnownAnswers))]
        public void PlatformProviderReportsATamperedKnownAnswer(KnownAnswer vector, string part)
        {
            PlatformSymmetricCryptoProvider provider = PlatformSymmetricCryptoProvider.Instance;

            byte[] ciphertext = CoreUtils.FromHexString(vector.Ciphertext);
            byte[] tag = CoreUtils.FromHexString(vector.Tag);
            byte[] associatedData = CoreUtils.FromHexString(vector.AssociatedData);
            Tamper(part, ciphertext, tag, ref associatedData);

            byte[] recovered = new byte[ciphertext.Length];
            recovered.AsSpan().Fill(0xCC);

            // Reported rather than thrown, so the channel raises a protocol
            // error; nothing unverified reaches the plaintext buffer.
            bool verified = provider.DecryptAuthenticated(
                vector.Algorithm,
                CoreUtils.FromHexString(vector.Key),
                CoreUtils.FromHexString(vector.Nonce),
                ciphertext,
                tag,
                recovered,
                associatedData);

            Assert.Multiple(() =>
            {
                Assert.That(verified, Is.False);
                Assert.That(Array.TrueForAll(recovered, b => b == 0), Is.True);
            });
        }

        public static IEnumerable<TestCaseData> TamperedKnownAnswers()
        {
            foreach (KnownAnswer vector in KnownAnswers())
            {
                foreach (string part in new[] { "tag", "ciphertext", "associated data" })
                {
                    yield return new TestCaseData(vector, part)
                        .SetArgDisplayNames(vector.Name, part);
                }
            }
        }

#if NETFRAMEWORK || AEAD_POLYFILL_CROSSCHECK
        [TestCaseSource(nameof(KnownAnswers))]
        public void PolyfillMatchesTheKnownAnswer(KnownAnswer vector)
        {
            byte[] key = CoreUtils.FromHexString(vector.Key);
            byte[] nonce = CoreUtils.FromHexString(vector.Nonce);
            byte[] associatedData = CoreUtils.FromHexString(vector.AssociatedData);
            byte[] plaintext = CoreUtils.FromHexString(vector.Plaintext);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];
            byte[] recovered = new byte[plaintext.Length];

            PolyfillEncrypt(vector.Algorithm, key, nonce, plaintext, ciphertext, tag, associatedData);
            PolyfillDecrypt(vector.Algorithm, key, nonce, ciphertext, tag, recovered, associatedData);

            Assert.Multiple(() =>
            {
                Assert.That(ciphertext, Is.EqualTo(CoreUtils.FromHexString(vector.Ciphertext)));
                Assert.That(tag, Is.EqualTo(CoreUtils.FromHexString(vector.Tag)));
                Assert.That(recovered, Is.EqualTo(plaintext));
            });
        }

        [TestCaseSource(nameof(TamperedKnownAnswers))]
        public void PolyfillThrowsACryptographicExceptionForATamperedKnownAnswer(
            KnownAnswer vector,
            string part)
        {
            byte[] ciphertext = CoreUtils.FromHexString(vector.Ciphertext);
            byte[] tag = CoreUtils.FromHexString(vector.Tag);
            byte[] associatedData = CoreUtils.FromHexString(vector.AssociatedData);
            Tamper(part, ciphertext, tag, ref associatedData);

            byte[] recovered = new byte[ciphertext.Length];
            recovered.AsSpan().Fill(0xCC);

            // The BCL type, not BouncyCastle's InvalidCipherTextException.
            Assert.That(
                () => PolyfillDecrypt(
                    vector.Algorithm,
                    CoreUtils.FromHexString(vector.Key),
                    CoreUtils.FromHexString(vector.Nonce),
                    ciphertext,
                    tag,
                    recovered,
                    associatedData),
                Throws.TypeOf<CryptographicException>());
            Assert.That(Array.TrueForAll(recovered, b => b == 0), Is.True);
        }

        [Test]
        public void PolyfillRejectsInvalidArguments()
        {
            byte[] key = new byte[32];
            byte[] block = new byte[16];

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new PolyfillAesGcm(new byte[20], 16),
                    Throws.TypeOf<CryptographicException>());
                Assert.That(
                    () => new PolyfillAesGcm(key, 8),
                    Throws.ArgumentException);
                Assert.That(
                    () => new PolyfillChaCha20Poly1305(new byte[16]),
                    Throws.TypeOf<CryptographicException>());
                Assert.That(
                    () =>
                    {
                        using var aesGcm = new PolyfillAesGcm(key, 16);
                        aesGcm.Encrypt(new byte[8], block, new byte[16], new byte[16]);
                    },
                    Throws.ArgumentException,
                    "The nonce is 96 bits.");
                Assert.That(
                    () =>
                    {
                        using var chaCha = new PolyfillChaCha20Poly1305(key);
                        chaCha.Encrypt(new byte[12], block, new byte[16], new byte[12]);
                    },
                    Throws.ArgumentException,
                    "The tag is 128 bits.");
                Assert.That(
                    () =>
                    {
                        using var chaCha = new PolyfillChaCha20Poly1305(key);
                        chaCha.Encrypt(new byte[12], block, new byte[15], new byte[16]);
                    },
                    Throws.ArgumentException,
                    "Ciphertext and plaintext have the same length.");
                Assert.That(
                    () =>
                    {
                        var aesGcm = new PolyfillAesGcm(key, 16);
                        aesGcm.Dispose();
                        aesGcm.Encrypt(new byte[12], block, new byte[16], new byte[16]);
                    },
                    Throws.TypeOf<ObjectDisposedException>());
            });
        }

        private static void PolyfillEncrypt(
            SymmetricEncryptionAlgorithm algorithm,
            byte[] key,
            byte[] nonce,
            byte[] plaintext,
            byte[] ciphertext,
            byte[] tag,
            byte[] associatedData)
        {
            if (algorithm == SymmetricEncryptionAlgorithm.ChaCha20Poly1305)
            {
                using var chaCha = new PolyfillChaCha20Poly1305(key);
                chaCha.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            }
            else
            {
                using var aesGcm = new PolyfillAesGcm(key, tag.Length);
                aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            }
        }

        private static void PolyfillDecrypt(
            SymmetricEncryptionAlgorithm algorithm,
            byte[] key,
            byte[] nonce,
            byte[] ciphertext,
            byte[] tag,
            byte[] plaintext,
            byte[] associatedData)
        {
            if (algorithm == SymmetricEncryptionAlgorithm.ChaCha20Poly1305)
            {
                using var chaCha = new PolyfillChaCha20Poly1305(key);
                chaCha.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            }
            else
            {
                using var aesGcm = new PolyfillAesGcm(key, tag.Length);
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            }
        }
#endif

#if AEAD_POLYFILL_CROSSCHECK
        /// <summary>
        /// The polyfill and the BCL agree on random inputs, including empty and
        /// partial block messages and associated data, and each decrypts what
        /// the other encrypted.
        /// </summary>
        [Test]
        public void PolyfillAgreesWithTheBcl(
            [Values(
                SymmetricEncryptionAlgorithm.Aes128Gcm,
                SymmetricEncryptionAlgorithm.Aes256Gcm,
                SymmetricEncryptionAlgorithm.ChaCha20Poly1305)]
                SymmetricEncryptionAlgorithm algorithm,
            [Values(0, 1, 15, 16, 17, 64, 1000, 8192)] int length,
            [Values(0, 7, 100)] int associatedLength)
        {
            byte[] key = new byte[algorithm == SymmetricEncryptionAlgorithm.Aes128Gcm ? 16 : 32];
            byte[] nonce = new byte[12];
            byte[] plaintext = new byte[length];
            byte[] associatedData = new byte[associatedLength];
            RandomNumberGenerator.Fill(key);
            RandomNumberGenerator.Fill(nonce);
            RandomNumberGenerator.Fill(plaintext);
            RandomNumberGenerator.Fill(associatedData);

            byte[] bclCiphertext = new byte[length];
            byte[] bclTag = new byte[16];
            if (algorithm == SymmetricEncryptionAlgorithm.ChaCha20Poly1305)
            {
                using var chaCha = new ChaCha20Poly1305(key);
                chaCha.Encrypt(nonce, plaintext, bclCiphertext, bclTag, associatedData);
            }
            else
            {
                using var aesGcm = new AesGcm(key, 16);
                aesGcm.Encrypt(nonce, plaintext, bclCiphertext, bclTag, associatedData);
            }

            byte[] polyfillCiphertext = new byte[length];
            byte[] polyfillTag = new byte[16];
            PolyfillEncrypt(
                algorithm, key, nonce, plaintext, polyfillCiphertext, polyfillTag, associatedData);

            byte[] recoveredByPolyfill = new byte[length];
            PolyfillDecrypt(
                algorithm, key, nonce, bclCiphertext, bclTag, recoveredByPolyfill, associatedData);

            byte[] recoveredByBcl = new byte[length];
            if (algorithm == SymmetricEncryptionAlgorithm.ChaCha20Poly1305)
            {
                using var chaCha = new ChaCha20Poly1305(key);
                chaCha.Decrypt(nonce, polyfillCiphertext, polyfillTag, recoveredByBcl, associatedData);
            }
            else
            {
                using var aesGcm = new AesGcm(key, 16);
                aesGcm.Decrypt(nonce, polyfillCiphertext, polyfillTag, recoveredByBcl, associatedData);
            }

            Assert.Multiple(() =>
            {
                Assert.That(polyfillCiphertext, Is.EqualTo(bclCiphertext));
                Assert.That(polyfillTag, Is.EqualTo(bclTag));
                Assert.That(recoveredByPolyfill, Is.EqualTo(plaintext));
                Assert.That(recoveredByBcl, Is.EqualTo(plaintext));
            });
        }
#endif

        private static void Tamper(
            string part,
            byte[] ciphertext,
            byte[] tag,
            ref byte[] associatedData)
        {
            switch (part)
            {
                case "tag":
                    tag[tag.Length - 1] ^= 0x01;
                    break;
                case "ciphertext":
                    ciphertext[0] ^= 0x80;
                    break;
                default:
                    // Some vectors have none; adding a byte changes it just the same.
                    associatedData = associatedData.Length == 0
                        ? [0x00]
                        : FlipFirst(associatedData);
                    break;
            }
        }

        private static byte[] FlipFirst(byte[] value)
        {
            value[0] ^= 0x01;
            return value;
        }
    }
}
