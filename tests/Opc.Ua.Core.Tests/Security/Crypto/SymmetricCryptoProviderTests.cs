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
using System.Collections.Generic;
using System.Security.Cryptography;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Security.Crypto
{
    /// <summary>
    /// Covers the symmetric, key derivation and random provider seams: that they
    /// are used when registered, that the platform path is taken when they are
    /// not, and that a provider bound to a purpose it cannot serve is caught
    /// rather than silently bypassed.
    /// </summary>
    [TestFixture]
    [Category("CryptoProvider")]
    [Parallelizable(ParallelScope.All)]
    [SetCulture("en-us")]
    public class SymmetricCryptoProviderTests
    {
        [Test]
        public void PlatformSymmetricProviderRoundTripsCbc()
        {
            byte[] key = new byte[32];
            byte[] iv = new byte[16];
            byte[] plaintext = new byte[64];
            FillRandom(key);
            FillRandom(iv);
            FillRandom(plaintext);

            byte[] ciphertext = new byte[plaintext.Length];
            byte[] recovered = new byte[plaintext.Length];

            PlatformSymmetricCryptoProvider provider = PlatformSymmetricCryptoProvider.Instance;

            provider.Encrypt(
                SymmetricEncryptionAlgorithm.Aes256Cbc, key, iv, plaintext, ciphertext);
            provider.Decrypt(
                SymmetricEncryptionAlgorithm.Aes256Cbc, key, iv, ciphertext, recovered);

            Assert.Multiple(() =>
            {
                Assert.That(ciphertext, Is.Not.EqualTo(plaintext));
                Assert.That(recovered, Is.EqualTo(plaintext));
            });
        }

        [Test]
        public void PlatformSymmetricProviderEncryptsInPlace()
        {
            byte[] key = new byte[16];
            byte[] iv = new byte[16];
            byte[] buffer = new byte[32];
            FillRandom(key);
            FillRandom(iv);
            FillRandom(buffer);

            byte[] expected = new byte[buffer.Length];
            PlatformSymmetricCryptoProvider provider = PlatformSymmetricCryptoProvider.Instance;

            provider.Encrypt(SymmetricEncryptionAlgorithm.Aes128Cbc, key, iv, buffer, expected);
            provider.Encrypt(SymmetricEncryptionAlgorithm.Aes128Cbc, key, iv, buffer, buffer);

            Assert.That(buffer, Is.EqualTo(expected), "in place must match out of place");
        }

        [Test]
        public void PlatformSymmetricProviderSignatureMatchesTheHmacTheChannelUses()
        {
            byte[] key = new byte[32];
            byte[] data = new byte[128];
            FillRandom(key);
            FillRandom(data);

            PlatformSymmetricCryptoProvider provider = PlatformSymmetricCryptoProvider.Instance;
            byte[] signature = new byte[
                provider.GetSignatureLength(SymmetricSignatureAlgorithm.HmacSha256)];

            provider.Sign(SymmetricSignatureAlgorithm.HmacSha256, key, data, signature);

            using var hmac = new HMACSHA256(key);
            byte[] expected = hmac.ComputeHash(data);

            Assert.Multiple(() =>
            {
                Assert.That(signature, Is.EqualTo(expected));
                Assert.That(
                    provider.Verify(
                        SymmetricSignatureAlgorithm.HmacSha256, key, data, signature),
                    Is.True);
            });
        }

        [Test]
        public void PlatformSymmetricProviderRejectsATamperedSignature()
        {
            byte[] key = new byte[32];
            byte[] data = new byte[64];
            FillRandom(key);
            FillRandom(data);

            PlatformSymmetricCryptoProvider provider = PlatformSymmetricCryptoProvider.Instance;
            byte[] signature = new byte[
                provider.GetSignatureLength(SymmetricSignatureAlgorithm.HmacSha384)];

            provider.Sign(SymmetricSignatureAlgorithm.HmacSha384, key, data, signature);
            signature[0] ^= 0xFF;

            Assert.That(
                provider.Verify(SymmetricSignatureAlgorithm.HmacSha384, key, data, signature),
                Is.False);
        }

        [Test]
        public void PlatformKeyDerivationMatchesTheChannelDerivation()
        {
            byte[] secret = new byte[32];
            byte[] seed = new byte[32];
            FillRandom(secret);
            FillRandom(seed);

            byte[] derived = new byte[64];
            PlatformKeyDerivationProvider.Instance.DeriveKey(
                KeyDerivationAlgorithm.PSha256, secret, seed, derived);

            byte[] expected = Utils.PSHA256(secret, null, seed, 0, derived.Length);

            Assert.That(derived, Is.EqualTo(expected));
        }

        [Test]
        public void PlatformKeyDerivationMatchesTheHkdfDerivation()
        {
            byte[] secret = new byte[32];
            byte[] salt = new byte[48];
            FillRandom(secret);
            FillRandom(salt);

            byte[] derived = new byte[80];
            PlatformKeyDerivationProvider.Instance.DeriveKey(
                KeyDerivationAlgorithm.HKDFSha256, secret, salt, derived);

            byte[] expected = Nonce.DeriveHkdfKeyData(
                secret, salt, KeyDerivationAlgorithm.HKDFSha256, derived.Length);

            Assert.That(derived, Is.EqualTo(expected));
        }

        /// <summary>
        /// AES counter mode is its own inverse, and the counter layout is fixed
        /// by Part 14 §7.2.4.4.3.2: a twelve byte nonce followed by a big endian
        /// block counter starting at zero.
        /// </summary>
        [Test]
        public void PlatformSymmetricProviderRoundTripsCounterMode()
        {
            byte[] key = new byte[32];
            byte[] nonce = new byte[12];
            // Deliberately not a whole number of blocks.
            byte[] plaintext = new byte[70];
            FillRandom(key);
            FillRandom(nonce);
            FillRandom(plaintext);

            byte[] ciphertext = new byte[plaintext.Length];
            byte[] recovered = new byte[plaintext.Length];

            PlatformSymmetricCryptoProvider provider = PlatformSymmetricCryptoProvider.Instance;

            provider.Encrypt(
                SymmetricEncryptionAlgorithm.Aes256Ctr, key, nonce, plaintext, ciphertext);
            provider.Decrypt(
                SymmetricEncryptionAlgorithm.Aes256Ctr, key, nonce, ciphertext, recovered);

            Assert.Multiple(() =>
            {
                Assert.That(provider.Supports(SymmetricEncryptionAlgorithm.Aes256Ctr), Is.True);
                Assert.That(ciphertext, Is.Not.EqualTo(plaintext));
                Assert.That(recovered, Is.EqualTo(plaintext));
            });
        }

        /// <summary>
        /// The counter must advance per block, or a long message would reuse key
        /// stream.
        /// </summary>
        [Test]
        public void PlatformSymmetricProviderCounterModeAdvancesPerBlock()
        {
            byte[] key = new byte[16];
            byte[] nonce = new byte[12];
            FillRandom(key);
            FillRandom(nonce);

            byte[] zeros = new byte[48];
            byte[] keyStream = new byte[zeros.Length];

            PlatformSymmetricCryptoProvider.Instance.Encrypt(
                SymmetricEncryptionAlgorithm.Aes128Ctr, key, nonce, zeros, keyStream);

            byte[] first = keyStream.AsSpan(0, 16).ToArray();
            byte[] second = keyStream.AsSpan(16, 16).ToArray();
            byte[] third = keyStream.AsSpan(32, 16).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(second, Is.Not.EqualTo(first), "block 2 reused block 1 key stream");
                Assert.That(third, Is.Not.EqualTo(second), "block 3 reused block 2 key stream");
            });
        }

        [Test]
        public void PlatformRandomSourceFillsTheWholeBuffer()
        {
            byte[] buffer = new byte[64];

            PlatformRandomSource.Instance.GetBytes(buffer);

            Assert.That(Array.TrueForAll(buffer, b => b == 0), Is.False);
        }

        /// <summary>
        /// The default configuration must take the inline path, so the per
        /// message code stays free of interface dispatch.
        /// </summary>
        [Test]
        public void ResolvingAFacetWithoutARegistryYieldsNothing()
        {
            Assert.Multiple(() =>
            {
                Assert.That(CryptoProviderFacets.ResolveSymmetric(null), Is.Null);
                Assert.That(CryptoProviderFacets.ResolveKeyDerivation(null), Is.Null);
                Assert.That(CryptoProviderFacets.ResolveRandom(null), Is.Null);
            });
        }

        /// <summary>
        /// The platform facets perform exactly the inline operations, so resolving
        /// them must still tell the caller to stay inline.
        /// </summary>
        [Test]
        public void ResolvingAFacetOnTheDefaultRegistryYieldsNothing()
        {
            var registry = new CryptoProviderRegistry();

            Assert.Multiple(() =>
            {
                Assert.That(CryptoProviderFacets.ResolveSymmetric(registry), Is.Null);
                Assert.That(CryptoProviderFacets.ResolveKeyDerivation(registry), Is.Null);
                Assert.That(CryptoProviderFacets.ResolveRandom(registry), Is.Null);
            });
        }

        [Test]
        public void ResolvingAFacetFindsARegisteredProvider()
        {
            var registry = new CryptoProviderRegistry();
            var counting = new CountingSymmetricProvider();

            new CryptoProviderBuilder(registry)
                .For(CryptoPurpose.ChannelSymmetric).Use(counting);

            Assert.That(
                CryptoProviderFacets.ResolveSymmetric(registry, SecurityPolicies.Basic256Sha256),
                Is.SameAs(counting));
        }

        /// <summary>
        /// A provider that declares the purpose but carries no facet must be
        /// bypassed, not crash the channel.
        /// </summary>
        [Test]
        public void ResolvingAFacetSkipsAProviderThatDoesNotCarryIt()
        {
            var registry = new CryptoProviderRegistry();
            var facetless = new FacetlessProvider(
                new CryptoCapability(CryptoPurpose.ChannelSymmetric));

            new CryptoProviderBuilder(registry)
                .For(CryptoPurpose.ChannelSymmetric).Use(facetless);

            Assert.That(CryptoProviderFacets.ResolveSymmetric(registry), Is.Null);
        }

        [Test]
        public void TheChannelPathUsesARegisteredSymmetricProvider()
        {
            SecurityPolicyInfo policy = SecurityPolicies.GetInfo(SecurityPolicies.Basic256Sha256)!;
            var counting = new CountingSymmetricProvider();

            byte[] encryptingKey = new byte[policy.SymmetricEncryptionKeyLength];
            byte[] signingKey = new byte[policy.DerivedSignatureKeyLength];
            byte[] iv = new byte[policy.InitializationVectorLength];
            FillRandom(encryptingKey);
            FillRandom(signingKey);
            FillRandom(iv);

            byte[] buffer = new byte[512];
            FillRandom(buffer);
            byte[] body = new byte[64];
            Buffer.BlockCopy(buffer, kHeaderSize, body, 0, body.Length);

            var data = new ArraySegment<byte>(buffer, kHeaderSize, body.Length);

            ArraySegment<byte> secured = CryptoUtils.SymmetricEncryptAndSign(
                data, policy, encryptingKey, iv, signingKey, null, false, 1, 1, counting);

            Assert.Multiple(() =>
            {
                Assert.That(counting.Encrypts, Is.EqualTo(1), "the provider must encrypt");
                Assert.That(counting.Signs, Is.EqualTo(1), "the provider must sign");
            });

            var toVerify = new ArraySegment<byte>(
                secured.Array!, kHeaderSize, secured.Count - kHeaderSize);

            ArraySegment<byte> recovered = CryptoUtils.SymmetricDecryptAndVerify(
                toVerify, policy, encryptingKey, iv, signingKey, false, 1, 1, null, counting);

            byte[] plaintext = new byte[body.Length];
            Buffer.BlockCopy(
                recovered.Array!, kHeaderSize, plaintext, 0, body.Length);

            Assert.Multiple(() =>
            {
                Assert.That(counting.Decrypts, Is.EqualTo(1), "the provider must decrypt");
                Assert.That(counting.Verifies, Is.EqualTo(1), "the provider must verify");
                Assert.That(plaintext, Is.EqualTo(body), "the round trip must recover the body");
            });
        }

        /// <summary>
        /// A message secured by the platform must be readable through a provider
        /// and back, or a validated module could not interoperate.
        /// </summary>
        [Test]
        public void AProviderAndThePlatformProduceTheSameBytes()
        {
            SecurityPolicyInfo policy = SecurityPolicies.GetInfo(SecurityPolicies.Basic256Sha256)!;

            byte[] encryptingKey = new byte[policy.SymmetricEncryptionKeyLength];
            byte[] signingKey = new byte[policy.DerivedSignatureKeyLength];
            byte[] iv = new byte[policy.InitializationVectorLength];
            FillRandom(encryptingKey);
            FillRandom(signingKey);
            FillRandom(iv);

            byte[] viaPlatform = new byte[512];
            FillRandom(viaPlatform);
            byte[] viaProvider = (byte[])viaPlatform.Clone();

            using HMAC hmac = policy.CreateSignatureHmac(signingKey)!;

            ArraySegment<byte> platformResult = CryptoUtils.SymmetricEncryptAndSign(
                new ArraySegment<byte>(viaPlatform, kHeaderSize, 64),
                policy, encryptingKey, iv, signingKey, hmac, false, 3, 7);

            ArraySegment<byte> providerResult = CryptoUtils.SymmetricEncryptAndSign(
                new ArraySegment<byte>(viaProvider, kHeaderSize, 64),
                policy, encryptingKey, iv, signingKey, null, false, 3, 7,
                new CountingSymmetricProvider());

            Assert.Multiple(() =>
            {
                Assert.That(providerResult, Has.Count.EqualTo(platformResult.Count));
                Assert.That(
                    viaProvider.AsSpan(0, providerResult.Count).ToArray(),
                    Is.EqualTo(viaPlatform.AsSpan(0, platformResult.Count).ToArray()));
            });
        }

        [Test]
        public void AnUnservedPurposeIsReportedWhenAProviderCannotPerformIt()
        {
            var registry = new CryptoProviderRegistry();
            var facetless = new FacetlessProvider(
                new CryptoCapability(CryptoPurpose.ChannelSymmetric));

            new CryptoProviderBuilder(registry)
                .For(CryptoPurpose.ChannelSymmetric).Use(facetless);

            var unserved = new List<string>();
            foreach (CryptoPurpose purpose in
                CryptoCompliance.GetUnservedOperationPurposes(registry))
            {
                unserved.Add(purpose.Name);
            }

            Assert.That(unserved, Does.Contain(CryptoPurpose.ChannelSymmetric.Name));
        }

        [Test]
        public void TheDefaultRegistryServesEveryOperationPurpose()
        {
            var registry = new CryptoProviderRegistry();

            Assert.That(
                CryptoCompliance.GetUnservedOperationPurposes(registry),
                Has.Count.Zero,
                "the platform provider carries every operation facet");
        }

        [Test]
        public void GetUnservedOperationPurposesRejectsANullRegistry()
        {
            Assert.Throws<ArgumentNullException>(
                () => CryptoCompliance.GetUnservedOperationPurposes(null!));
        }

        private const int kHeaderSize = 24;

        /// <summary>
        /// Fills a buffer with random bytes on every supported target framework.
        /// </summary>
        /// <remarks>
        /// <c>RandomNumberGenerator.Fill</c> does not exist on the .NET Framework
        /// targets this project still builds for.
        /// </remarks>
        private static void FillRandom(byte[] buffer)
        {
            using RandomNumberGenerator rng = RandomNumberGenerator.Create();
            rng.GetBytes(buffer);
        }

        /// <summary>
        /// Delegates to the platform but records that it was asked, which is how
        /// the tests prove the seam is actually taken.
        /// </summary>
        private sealed class CountingSymmetricProvider : ICryptoProvider, ISymmetricCryptoProvider
        {
            public string Name => "Counting";

            public CryptoValidationStatus Validation => new(
                CryptoValidationLevel.FipsValidated, "Test module", "CMVP #0000");

            public ArrayOf<CryptoCapability> Capabilities { get; } =
                new(new[] { new CryptoCapability(CryptoPurpose.ChannelSymmetric) });

            public int Encrypts { get; private set; }

            public int Decrypts { get; private set; }

            public int Signs { get; private set; }

            public int Verifies { get; private set; }

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
                Encrypts++;
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
                Decrypts++;
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
                Verifies++;
                return PlatformSymmetricCryptoProvider.Instance
                    .Verify(algorithm, key, data, signature);
            }
        }

        /// <summary>
        /// Declares a purpose it cannot actually perform, which is the
        /// configuration mistake the compliance check exists to catch.
        /// </summary>
        private sealed class FacetlessProvider : ICryptoProvider
        {
            public FacetlessProvider(CryptoCapability capability)
            {
                Capabilities = new ArrayOf<CryptoCapability>(new[] { capability });
            }

            public string Name => "Facetless";

            public CryptoValidationStatus Validation => CryptoValidationStatus.Platform;

            public ArrayOf<CryptoCapability> Capabilities { get; }
        }
    }

    /// <summary>
    /// Covers how a random source binding reaches <see cref="Nonce"/>. The
    /// nonce source is process-wide, so these cases cannot run in parallel with
    /// anything that draws a nonce.
    /// </summary>
    [TestFixture]
    [Category("CryptoProvider")]
    [NonParallelizable]
    [SetCulture("en-us")]
    public class RandomSourceBindingTests
    {
        [TearDown]
        public void ResetNonceSource()
        {
            Nonce.SetRandomSource(null);
        }

        [Test]
        public void UnscopedRandomBindingBecomesTheNonceSource()
        {
            var source = new CountingRandomSource();
            var registry = new CryptoProviderRegistry();

            new CryptoProviderBuilder(registry)
                .For(CryptoPurpose.RandomNumberGeneration)
                .Use(source);

            Nonce.CreateRandomNonceData(32, false, null);

            Assert.That(source.Calls, Is.GreaterThan(0));
        }

        [Test]
        public void PolicyScopedRandomBindingDoesNotEscapeItsPolicy()
        {
            var source = new CountingRandomSource();
            var registry = new CryptoProviderRegistry();

            new CryptoProviderBuilder(registry)
                .For(CryptoPurpose.RandomNumberGeneration, SecurityPolicies.Basic256Sha256)
                .Use(source);

            Nonce.CreateRandomNonceData(32, false, null);

            Assert.Multiple(() =>
            {
                Assert.That(source.Calls, Is.Zero,
                    "A binding scoped to one security policy must not redirect every nonce in the process.");
                Assert.That(
                    registry.Resolve(
                        CryptoPurpose.RandomNumberGeneration,
                        SecurityPolicies.Basic256Sha256),
                    Is.SameAs(source),
                    "The scoped binding must still be registered for its own policy.");
            });
        }

        private sealed class CountingRandomSource : ICryptoProvider, IRandomSource
        {
            public int Calls { get; private set; }

            public string Name => "Counting";

            public CryptoValidationStatus Validation => CryptoValidationStatus.Platform;

            public ArrayOf<CryptoCapability> Capabilities { get; } =
                new(new[] { new CryptoCapability(CryptoPurpose.RandomNumberGeneration) });

            public void GetBytes(Span<byte> buffer)
            {
                Calls++;
                PlatformRandomSource.Instance.GetBytes(buffer);
            }
        }
    }
}
