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
using System.Security.Cryptography;
using NUnit.Framework;
using Opc.Ua.PubSub.Security.Policies;

namespace Opc.Ua.PubSub.Tests.Security.Policies
{
    /// <summary>
    /// Covers routing the PubSub per-message cryptography through a symmetric
    /// crypto provider, so a validated module can perform it.
    /// </summary>
    /// <remarks>
    /// The property that matters is not only that the provider is called, but
    /// that it produces the same bytes as the platform. A validated module has to
    /// interoperate with publishers and subscribers that use neither.
    /// </remarks>
    [TestFixture]
    [Category("PubSubSecurity")]
    [Parallelizable(ParallelScope.All)]
    [SetCulture("en-us")]
    public class PubSubPolicyProviderTests
    {
        [Test]
        public void PolicyUsesTheRegisteredProviderAndAgreesWithThePlatform()
        {
            var counting = new CountingSymmetricProvider();
            var withProvider = new PubSubAes256CtrPolicy(counting);
            var platform = new PubSubAes256CtrPolicy();

            byte[] signingKey = new byte[withProvider.SigningKeyLength];
            byte[] encryptingKey = new byte[withProvider.EncryptingKeyLength];
            byte[] nonce = new byte[withProvider.NonceLength];
            byte[] plaintext = new byte[93];
            Fill(signingKey);
            Fill(encryptingKey);
            Fill(nonce);
            Fill(plaintext);

            byte[] viaProvider = new byte[plaintext.Length];
            byte[] viaPlatform = new byte[plaintext.Length];
            withProvider.Encrypt(plaintext, encryptingKey, nonce, viaProvider);
            platform.Encrypt(plaintext, encryptingKey, nonce, viaPlatform);

            byte[] providerSignature = new byte[withProvider.SignatureLength];
            byte[] platformSignature = new byte[platform.SignatureLength];
            withProvider.Sign(viaProvider, signingKey, providerSignature);
            platform.Sign(viaPlatform, signingKey, platformSignature);

            byte[] recovered = new byte[plaintext.Length];
            withProvider.Decrypt(viaProvider, encryptingKey, nonce, recovered);

            Assert.Multiple(() =>
            {
                Assert.That(counting.Encrypts, Is.EqualTo(1), "the provider must encrypt");
                Assert.That(counting.Decrypts, Is.EqualTo(1), "the provider must decrypt");
                Assert.That(counting.Signs, Is.EqualTo(1), "the provider must sign");
                Assert.That(
                    viaProvider,
                    Is.EqualTo(viaPlatform),
                    "a module that disagrees with the platform cannot interoperate");
                Assert.That(providerSignature, Is.EqualTo(platformSignature));
                Assert.That(recovered, Is.EqualTo(plaintext));
            });
        }

        [Test]
        public void PolicyVerifiesThroughTheProvider()
        {
            var counting = new CountingSymmetricProvider();
            var policy = new PubSubAes128CtrPolicy(counting);

            byte[] signingKey = new byte[policy.SigningKeyLength];
            byte[] data = new byte[64];
            Fill(signingKey);
            Fill(data);

            byte[] signature = new byte[policy.SignatureLength];
            policy.Sign(data, signingKey, signature);

            Assert.Multiple(() =>
            {
                Assert.That(policy.Verify(data, signature, signingKey), Is.True);
                Assert.That(counting.Signs, Is.GreaterThanOrEqualTo(2), "sign and verify both use it");
            });

            signature[0] ^= 0xFF;
            Assert.That(policy.Verify(data, signature, signingKey), Is.False);
        }

        /// <summary>
        /// A provider that cannot serve the algorithms this policy needs must be
        /// ignored rather than used, so a configuration mistake does not break
        /// publishing.
        /// </summary>
        [Test]
        public void PolicyIgnoresAProviderThatCannotServeItsAlgorithms()
        {
            var refusing = new RefusingSymmetricProvider();
            var policy = new PubSubAes256CtrPolicy(refusing);
            var platform = new PubSubAes256CtrPolicy();

            byte[] encryptingKey = new byte[policy.EncryptingKeyLength];
            byte[] nonce = new byte[policy.NonceLength];
            byte[] plaintext = new byte[32];
            Fill(encryptingKey);
            Fill(nonce);
            Fill(plaintext);

            byte[] actual = new byte[plaintext.Length];
            byte[] expected = new byte[plaintext.Length];
            policy.Encrypt(plaintext, encryptingKey, nonce, actual);
            platform.Encrypt(plaintext, encryptingKey, nonce, expected);

            Assert.That(actual, Is.EqualTo(expected));
        }

        private static void Fill(byte[] buffer)
        {
            using RandomNumberGenerator rng = RandomNumberGenerator.Create();
            rng.GetBytes(buffer);
        }

        /// <summary>
        /// Delegates to the platform but records that it was asked.
        /// </summary>
        private sealed class CountingSymmetricProvider : ISymmetricCryptoProvider
        {
            public int Encrypts { get; private set; }

            public int Decrypts { get; private set; }

            public int Signs { get; private set; }

            public bool Supports(SymmetricEncryptionAlgorithm algorithm)
            {
                return PlatformSymmetricCryptoProvider.Instance.Supports(algorithm);
            }

            public bool Supports(SymmetricSignatureAlgorithm algorithm)
            {
                return PlatformSymmetricCryptoProvider.Instance.Supports(algorithm);
            }

            public void Encrypt(
                SymmetricEncryptionAlgorithm algorithm,
                ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> iv,
                ReadOnlySpan<byte> plaintext,
                Span<byte> ciphertext)
            {
                Encrypts++;
                PlatformSymmetricCryptoProvider.Instance
                    .Encrypt(algorithm, key, iv, plaintext, ciphertext);
            }

            public void Decrypt(
                SymmetricEncryptionAlgorithm algorithm,
                ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> iv,
                ReadOnlySpan<byte> ciphertext,
                Span<byte> plaintext)
            {
                Decrypts++;
                PlatformSymmetricCryptoProvider.Instance
                    .Decrypt(algorithm, key, iv, ciphertext, plaintext);
            }

            public void EncryptAuthenticated(
                SymmetricEncryptionAlgorithm algorithm,
                ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> nonce,
                ReadOnlySpan<byte> plaintext,
                Span<byte> ciphertext,
                Span<byte> tag,
                ReadOnlySpan<byte> associatedData)
            {
                PlatformSymmetricCryptoProvider.Instance.EncryptAuthenticated(
                    algorithm, key, nonce, plaintext, ciphertext, tag, associatedData);
            }

            public bool DecryptAuthenticated(
                SymmetricEncryptionAlgorithm algorithm,
                ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> nonce,
                ReadOnlySpan<byte> ciphertext,
                ReadOnlySpan<byte> tag,
                Span<byte> plaintext,
                ReadOnlySpan<byte> associatedData)
            {
                return PlatformSymmetricCryptoProvider.Instance.DecryptAuthenticated(
                    algorithm, key, nonce, ciphertext, tag, plaintext, associatedData);
            }

            public int GetSignatureLength(SymmetricSignatureAlgorithm algorithm)
            {
                return PlatformSymmetricCryptoProvider.Instance.GetSignatureLength(algorithm);
            }

            public void Sign(
                SymmetricSignatureAlgorithm algorithm,
                ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> data,
                Span<byte> signature)
            {
                Signs++;
                PlatformSymmetricCryptoProvider.Instance.Sign(algorithm, key, data, signature);
            }

            public bool Verify(
                SymmetricSignatureAlgorithm algorithm,
                ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> data,
                ReadOnlySpan<byte> signature)
            {
                return PlatformSymmetricCryptoProvider.Instance
                    .Verify(algorithm, key, data, signature);
            }
        }

        /// <summary>
        /// Declares that it can serve nothing, which is how a partial
        /// implementation says so.
        /// </summary>
        private sealed class RefusingSymmetricProvider : ISymmetricCryptoProvider
        {
            public bool Supports(SymmetricEncryptionAlgorithm algorithm)
            {
                return false;
            }

            public bool Supports(SymmetricSignatureAlgorithm algorithm)
            {
                return false;
            }

            public void Encrypt(
                SymmetricEncryptionAlgorithm algorithm,
                ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> iv,
                ReadOnlySpan<byte> plaintext,
                Span<byte> ciphertext)
            {
                throw new NotSupportedException();
            }

            public void Decrypt(
                SymmetricEncryptionAlgorithm algorithm,
                ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> iv,
                ReadOnlySpan<byte> ciphertext,
                Span<byte> plaintext)
            {
                throw new NotSupportedException();
            }

            public void EncryptAuthenticated(
                SymmetricEncryptionAlgorithm algorithm,
                ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> nonce,
                ReadOnlySpan<byte> plaintext,
                Span<byte> ciphertext,
                Span<byte> tag,
                ReadOnlySpan<byte> associatedData)
            {
                throw new NotSupportedException();
            }

            public bool DecryptAuthenticated(
                SymmetricEncryptionAlgorithm algorithm,
                ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> nonce,
                ReadOnlySpan<byte> ciphertext,
                ReadOnlySpan<byte> tag,
                Span<byte> plaintext,
                ReadOnlySpan<byte> associatedData)
            {
                throw new NotSupportedException();
            }

            public int GetSignatureLength(SymmetricSignatureAlgorithm algorithm)
            {
                throw new NotSupportedException();
            }

            public void Sign(
                SymmetricSignatureAlgorithm algorithm,
                ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> data,
                Span<byte> signature)
            {
                throw new NotSupportedException();
            }

            public bool Verify(
                SymmetricSignatureAlgorithm algorithm,
                ReadOnlySpan<byte> key,
                ReadOnlySpan<byte> data,
                ReadOnlySpan<byte> signature)
            {
                throw new NotSupportedException();
            }
        }
    }
}
