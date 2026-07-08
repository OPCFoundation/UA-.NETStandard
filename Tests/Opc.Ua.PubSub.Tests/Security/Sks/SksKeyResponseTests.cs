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
            byte[][] packed = new[] { new byte[] { 1, 2, 3 } };
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
                PubSubSecurityPolicyRegistry.GetByUri(PubSubSecurityPolicyUri.PubSubAes128Ctr)!;
            int total = policy.SigningKeyLength + policy.EncryptingKeyLength + policy.NonceLength;
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
            Assert.That(unpacked[0].KeyNonce.Length, Is.EqualTo(policy.NonceLength));

            byte[] firstSigning = unpacked[0].SigningKey.Span.ToArray();
            for (int i = 0; i < policy.SigningKeyLength; i++)
            {
                Assert.That(firstSigning[i], Is.EqualTo((byte)i));
            }
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
                PubSubSecurityPolicyRegistry.GetByUri(PubSubSecurityPolicyUri.PubSubAes128Ctr)!;
            int total = policy.SigningKeyLength + policy.EncryptingKeyLength + policy.NonceLength;
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
    }
}
