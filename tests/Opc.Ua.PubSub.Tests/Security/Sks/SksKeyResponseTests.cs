// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.PubSub.Security.Sks;

namespace Opc.Ua.PubSub.Tests.Security.Sks
{
    /// <summary>
    /// Tests for <see cref="SksKeyResponse"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("8.3.2")]
    public class SksKeyResponseTests
    {
        [Test]
        public void Constructor_RecordsAllFields()
        {
            byte[][] packed = [[1, 2, 3]];
            var response = new SksKeyResponse(
                PubSubSecurityPolicyUri.None,
                42U,
                packed,
                TimeSpan.FromSeconds(15),
                TimeSpan.FromMinutes(5));
            Assert.That(response.SecurityPolicyUri, Is.EqualTo(PubSubSecurityPolicyUri.None));
            Assert.That(response.FirstTokenId, Is.EqualTo(42U));
            byte[][]? responseKeys = (byte[][]?)response.Keys;
            Assert.That(responseKeys, Is.EqualTo(packed));
            Assert.That(response.TimeToNextKey, Is.EqualTo(TimeSpan.FromSeconds(15)));
            Assert.That(response.KeyLifetime, Is.EqualTo(TimeSpan.FromMinutes(5)));
        }

        [Test]
        public void Constructor_RejectsNullPolicyUri()
        {
            Assert.That(
                () => new SksKeyResponse(
                    null!,
                    1U,
                    Array.Empty<byte[]>(),
                    TimeSpan.Zero,
                    TimeSpan.FromMinutes(1)),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Constructor_DefaultKeys_AreNullArrayOf()
        {
            SksKeyResponse response = new(
                PubSubSecurityPolicyUri.None,
                1U,
                default,
                TimeSpan.Zero,
                TimeSpan.FromMinutes(1));
            Assert.That(response.Keys.IsNull, Is.True);
        }

        [Test]
        public void Constructor_RejectsNonPositiveKeyLifetime()
        {
            Assert.That(
                () => new SksKeyResponse(
                    PubSubSecurityPolicyUri.None,
                    1U,
                    Array.Empty<byte[]>(),
                    TimeSpan.Zero,
                    TimeSpan.Zero),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Unpacked_ReturnsEmptyForNonePolicy()
        {
            var response = new SksKeyResponse(
                PubSubSecurityPolicyUri.None,
                1U,
                new[] { Array.Empty<byte>() },
                TimeSpan.Zero,
                TimeSpan.FromMinutes(1));
            Assert.That(((PubSubSecurityKey[]?)response.Unpacked) ?? [], Is.Empty);
        }

        [Test]
        public void Unpacked_SplitsPackedKeysUsingPolicyLengths()
        {
            IPubSubSecurityPolicy policy =
                PubSubSecurityPolicyRegistry.Default.GetByUri(PubSubSecurityPolicyUri.PubSubAes128Ctr)!;
            int total = policy.SigningKeyLength + policy.EncryptingKeyLength + AesCtrNonceLayout.GetKeyNonceLength(policy);
            byte[] packed1 = new byte[total];
            byte[] packed2 = new byte[total];
            for (int i = 0; i < total; i++)
            {
                packed1[i] = (byte)i;
                packed2[i] = (byte)(i + 0x40);
            }

            var response = new SksKeyResponse(
                PubSubSecurityPolicyUri.PubSubAes128Ctr,
                10U,
                new[] { packed1, packed2 },
                TimeSpan.Zero,
                TimeSpan.FromMinutes(1));

            ArrayOf<PubSubSecurityKey> unpacked = response.Unpacked;
            Assert.That(unpacked.Count, Is.EqualTo(2));
            Assert.That(unpacked[0].TokenId, Is.EqualTo(10U));
            Assert.That(unpacked[1].TokenId, Is.EqualTo(11U));
            Assert.That(unpacked[0].SigningKey.Length, Is.EqualTo(policy.SigningKeyLength));
            Assert.That(unpacked[0].EncryptingKey.Length, Is.EqualTo(policy.EncryptingKeyLength));
            Assert.That(unpacked[0].KeyNonce.Length, Is.EqualTo(AesCtrNonceLayout.GetKeyNonceLength(policy)));

            byte[] firstSigning = unpacked[0].SigningKey.Span.ToArray();
            for (int i = 0; i < policy.SigningKeyLength; i++)
            {
                Assert.That(firstSigning[i], Is.EqualTo((byte)i));
            }
        }

        [TestCase(PubSubSecurityPolicyUri.PubSubAes128Ctr, 32 + 16 + 4)]
        [TestCase(PubSubSecurityPolicyUri.PubSubAes256Ctr, 32 + 32 + 4)]
        public void Unpacked_SplitsSpecKeyDataWithFourByteKeyNonce(string policyUri, int keyDataLength)
        {
            // OPC 10000-14 7.2.4.4.3.1 / Table 157: the key data is
            // SigningKey || EncryptingKey || KeyNonce with a 4-byte KeyNonce.
            byte[] packed = new byte[keyDataLength];
            for (int i = 0; i < packed.Length; i++)
            {
                packed[i] = (byte)i;
            }

            var response = new SksKeyResponse(
                policyUri,
                1U,
                new[] { packed },
                TimeSpan.Zero,
                TimeSpan.FromMinutes(1));

            ArrayOf<PubSubSecurityKey> unpacked = response.Unpacked;
            Assert.That(unpacked.Count, Is.EqualTo(1));
            Assert.That(
                unpacked[0].KeyNonce.Span.ToArray(),
                Is.EqualTo(packed.AsSpan(keyDataLength - 4).ToArray()));
        }

        [Test]
        public void Unpacked_RejectsWrongLengthPackedKey()
        {
            var response = new SksKeyResponse(
                PubSubSecurityPolicyUri.PubSubAes128Ctr,
                1U,
                new[] { new byte[3] },
                TimeSpan.Zero,
                TimeSpan.FromMinutes(1));
            Assert.That(() => response.Unpacked, Throws.InvalidOperationException);
        }

        [Test]
        public void Unpacked_IsCachedBetweenInvocations()
        {
            IPubSubSecurityPolicy policy =
                PubSubSecurityPolicyRegistry.Default.GetByUri(PubSubSecurityPolicyUri.PubSubAes128Ctr)!;
            int total = policy.SigningKeyLength + policy.EncryptingKeyLength + AesCtrNonceLayout.GetKeyNonceLength(policy);
            var response = new SksKeyResponse(
                PubSubSecurityPolicyUri.PubSubAes128Ctr,
                1U,
                new[] { new byte[total] },
                TimeSpan.Zero,
                TimeSpan.FromMinutes(1));
            ArrayOf<PubSubSecurityKey> first = response.Unpacked;
            ArrayOf<PubSubSecurityKey> second = response.Unpacked;
            var firstKeys = (PubSubSecurityKey[]?)first;
            var secondKeys = (PubSubSecurityKey[]?)second;
            Assert.That(secondKeys, Is.EqualTo(firstKeys));
        }

        [Test]
        public void UnpackUsesSuppliedTimeProviderForIssuedAt()
        {
            IPubSubSecurityPolicy policy =
                PubSubSecurityPolicyRegistry.Default.GetByUri(PubSubSecurityPolicyUri.PubSubAes128Ctr)!;
            int total = policy.SigningKeyLength + policy.EncryptingKeyLength + AesCtrNonceLayout.GetKeyNonceLength(policy);
            var response = new SksKeyResponse(
                PubSubSecurityPolicyUri.PubSubAes128Ctr,
                1U,
                new[] { new byte[total] },
                TimeSpan.Zero,
                TimeSpan.FromMinutes(1));
            var expected = new DateTimeOffset(2035, 4, 3, 2, 1, 0, TimeSpan.Zero);
            var timeProvider = new FakeTimeProvider(expected);

            ArrayOf<PubSubSecurityKey> unpacked = response.Unpack(timeProvider);

            Assert.That(unpacked[0].IssuedAt, Is.EqualTo(DateTimeUtc.From(expected.UtcDateTime)));
        }
    }
}
