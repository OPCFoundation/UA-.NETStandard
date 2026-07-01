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
