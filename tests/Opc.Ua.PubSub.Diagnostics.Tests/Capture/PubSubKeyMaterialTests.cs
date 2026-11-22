// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using NUnit.Framework;

namespace Opc.Ua.PubSub.Pcap.Tests
{
    /// <summary>
    /// Unit tests for <see cref="PubSubKeyMaterial"/> (defensive copy and
    /// zeroization of captured PubSub key material).
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    public sealed class PubSubKeyMaterialTests
    {
        [Test]
        public void ExposesSuppliedKeyMaterial()
        {
            byte[] signing = [1, 2, 3, 4];
            byte[] encrypting = [5, 6, 7, 8];
            byte[] nonce = [9, 10, 11, 12];

            using var key = new PubSubKeyMaterial(
                "group-1", 42u, "urn:policy:aes128ctr", signing, encrypting, nonce);

            Assert.Multiple(() =>
            {
                Assert.That(key.SecurityGroupId, Is.EqualTo("group-1"));
                Assert.That(key.TokenId, Is.EqualTo(42u));
                Assert.That(key.SecurityPolicyUri, Is.EqualTo("urn:policy:aes128ctr"));
                Assert.That(key.SigningKey.ToArray(), Is.EqualTo(signing));
                Assert.That(key.EncryptingKey.ToArray(), Is.EqualTo(encrypting));
                Assert.That(key.KeyNonce.ToArray(), Is.EqualTo(nonce));
            });
        }

        [Test]
        public void DefensivelyCopiesInputArrays()
        {
            byte[] signing = [1, 2, 3, 4];
            using var key = new PubSubKeyMaterial(
                "g", 1u, "p", signing, null, null);

            signing[0] = 0xFF;

            Assert.That(key.SigningKey.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        }

        [Test]
        public void DisposeZeroesAndEmptiesKeyMaterial()
        {
            var key = new PubSubKeyMaterial(
                "g", 1u, "p", [1, 2, 3, 4], [5, 6, 7, 8], [9, 10]);

            key.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(key.SigningKey.IsEmpty, Is.True);
                Assert.That(key.EncryptingKey.IsEmpty, Is.True);
                Assert.That(key.KeyNonce.IsEmpty, Is.True);
            });
        }

        [Test]
        public void DisposeIsIdempotent()
        {
            var key = new PubSubKeyMaterial("g", 1u, "p", [1], [2], [3]);
            key.Dispose();
            Assert.That(key.Dispose, Throws.Nothing);
        }

        [Test]
        public void NullSecurityGroupIdThrows()
        {
            Assert.That(
                () => new PubSubKeyMaterial(null!, 1u, "p", null, null, null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void NullPolicyUriThrows()
        {
            Assert.That(
                () => new PubSubKeyMaterial("g", 1u, null!, null, null, null),
                Throws.TypeOf<ArgumentNullException>());
        }
    }
}
