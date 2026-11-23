// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using NUnit.Framework;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.PubSub.Security.Sks;

namespace Opc.Ua.PubSub.Tests.Security.Sks
{
    /// <summary>
    /// Tests for <see cref="SksKeyGenerator"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("8.3.1")]
    public class SksKeyGeneratorTests
    {
        [Test]
        public void Generate_ProducesKeysOfPolicyLengths()
        {
            IPubSubSecurityPolicy policy =
                PubSubSecurityPolicyRegistry.Default.GetByUri(PubSubSecurityPolicyUri.PubSubAes256Ctr)!;
            var now = DateTimeUtc.From(DateTime.UtcNow);
            PubSubSecurityKey key = SksKeyGenerator.Generate(
                policy,
                7U,
                now,
                TimeSpan.FromMinutes(2));

            Assert.That(key.TokenId, Is.EqualTo(7U));
            Assert.That(key.IssuedAt, Is.EqualTo(now));
            Assert.That(key.Lifetime, Is.EqualTo(TimeSpan.FromMinutes(2)));
            Assert.That(key.SigningKey.Length, Is.EqualTo(policy.SigningKeyLength));
            Assert.That(key.EncryptingKey.Length, Is.EqualTo(policy.EncryptingKeyLength));
            Assert.That(key.KeyNonce.Length, Is.EqualTo(AesCtrNonceLayout.GetKeyNonceLength(policy)));
        }

        [Test]
        public void Generate_ProducesUniqueMaterialAcrossInvocations()
        {
            IPubSubSecurityPolicy policy =
                PubSubSecurityPolicyRegistry.Default.GetByUri(PubSubSecurityPolicyUri.PubSubAes128Ctr)!;
            var now = DateTimeUtc.From(DateTime.UtcNow);
            PubSubSecurityKey first = SksKeyGenerator.Generate(policy, 1U, now, TimeSpan.FromMinutes(1));
            PubSubSecurityKey second = SksKeyGenerator.Generate(policy, 2U, now, TimeSpan.FromMinutes(1));
            Assert.That(
                first.SigningKey.Span.ToArray(),
                Is.Not.EqualTo(second.SigningKey.Span.ToArray()));
            Assert.That(
                first.EncryptingKey.Span.ToArray(),
                Is.Not.EqualTo(second.EncryptingKey.Span.ToArray()));
        }

        [Test]
        public void Generate_RejectsNullPolicy()
        {
            Assert.That(
                () => SksKeyGenerator.Generate(
                    null!,
                    1U,
                    DateTimeUtc.From(DateTime.UtcNow),
                    TimeSpan.FromMinutes(1)),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Pack_RoundTripsThroughPolicyLengths()
        {
            IPubSubSecurityPolicy policy =
                PubSubSecurityPolicyRegistry.Default.GetByUri(PubSubSecurityPolicyUri.PubSubAes128Ctr)!;
            var now = DateTimeUtc.From(DateTime.UtcNow);
            PubSubSecurityKey key = SksKeyGenerator.Generate(policy, 1U, now, TimeSpan.FromMinutes(1));
            byte[] packed = SksKeyGenerator.Pack(key);
            int total = policy.SigningKeyLength + policy.EncryptingKeyLength + AesCtrNonceLayout.GetKeyNonceLength(policy);
            Assert.That(packed, Has.Length.EqualTo(total));

            byte[] signing = key.SigningKey.Span.ToArray();
            for (int i = 0; i < signing.Length; i++)
            {
                Assert.That(packed[i], Is.EqualTo(signing[i]));
            }
        }

        [Test]
        public void Pack_RejectsNullKey()
        {
            Assert.That(
                () => SksKeyGenerator.Pack(null!),
                Throws.TypeOf<ArgumentNullException>());
        }
    }
}
