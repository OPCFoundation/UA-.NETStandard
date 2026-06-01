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
 *
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
using Opc.Ua.Identity;

namespace Opc.Ua.Core.Tests.Security.Identity
{
    [TestFixture]
    [Category("Identity")]
    [Parallelizable]
    public class StaticIssuerKeyResolverTests
    {
        [Test]
        public async Task GetKeysAsyncReturnsKnownKid()
        {
            using var resolver = new StaticIssuerKeyResolver(
                "https://issuer.example.test",
                new[] { CreateKey("kid-1"), CreateKey("kid-2") });

            System.Collections.Generic.IReadOnlyList<IssuerVerificationKey> keys = await resolver
                .GetKeysAsync("kid-1")
                .ConfigureAwait(false);

            Assert.That(keys, Has.Count.EqualTo(1));
            Assert.That(keys[0].KeyId, Is.EqualTo("kid-1"));
        }

        [Test]
        public async Task GetKeysAsyncReturnsEmptyForUnknownKid()
        {
            using var resolver = new StaticIssuerKeyResolver(
                "https://issuer.example.test",
                new[] { CreateKey("kid-1") });

            System.Collections.Generic.IReadOnlyList<IssuerVerificationKey> keys = await resolver
                .GetKeysAsync("KID-1")
                .ConfigureAwait(false);

            Assert.That(keys, Is.Empty);
        }

        [Test]
        public async Task GetKeysAsyncReturnsAllKeysForNullKid()
        {
            using var resolver = new StaticIssuerKeyResolver(
                "https://issuer.example.test",
                new[] { CreateKey("kid-1"), CreateKey("kid-2") });

            System.Collections.Generic.IReadOnlyList<IssuerVerificationKey> keys = await resolver
                .GetKeysAsync(null)
                .ConfigureAwait(false);

            Assert.That(keys, Has.Count.EqualTo(2));
        }

        [Test]
        public void DisposeDisposesContainedKeys()
        {
            IssuerVerificationKey key = CreateKey("kid-1");
            var resolver = new StaticIssuerKeyResolver(
                "https://issuer.example.test",
                new[] { key });

            resolver.Dispose();

            Assert.That(
                () => key.VerifySignature([1], [1]),
                Throws.TypeOf<ObjectDisposedException>());
        }

#pragma warning disable CA2000 // IssuerVerificationKey owns the RSA instance; TODO: replace with ownership annotations when available.
        private static IssuerVerificationKey CreateKey(string kid)
        {
            var rsa = RSA.Create(2048);
            return new IssuerVerificationKey(kid, rsa, "RS256");
        }
#pragma warning restore CA2000
    }
}
