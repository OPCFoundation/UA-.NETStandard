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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Security;

namespace Opc.Ua.PubSub.Tests.Security
{
    /// <summary>
    /// Tests for <see cref="StaticSecurityKeyProvider"/>.
    /// </summary>
    [TestFixture]
    public class StaticSecurityKeyProviderTests
    {
        [Test]
        public async Task GetCurrentKeyAsync_ReturnsRingsCurrentKey()
        {
            var ring = new PubSubSecurityKeyRing("g");
            PubSubSecurityKey key = TestSecurityKeyFactory.Create(7U);
            ring.SetCurrent(key);
            var provider = new StaticSecurityKeyProvider("g", ring);
            PubSubSecurityKey result = await provider.GetCurrentKeyAsync();
            Assert.That(result, Is.SameAs(key));
        }

        [Test]
        public void GetCurrentKeyAsync_ThrowsWhenRingEmpty()
        {
            var ring = new PubSubSecurityKeyRing("g");
            var provider = new StaticSecurityKeyProvider("g", ring);
            Assert.That(
                async () => await provider.GetCurrentKeyAsync(),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task TryGetKeyAsync_ReturnsKeyForKnownToken()
        {
            var ring = new PubSubSecurityKeyRing("g");
            PubSubSecurityKey key = TestSecurityKeyFactory.Create(42U);
            ring.SetCurrent(key);
            var provider = new StaticSecurityKeyProvider("g", ring);
            PubSubSecurityKey? result = await provider.TryGetKeyAsync(42U);
            Assert.That(result, Is.SameAs(key));
        }

        [Test]
        public async Task TryGetKeyAsync_ReturnsNullForUnknownToken()
        {
            var ring = new PubSubSecurityKeyRing("g");
            var provider = new StaticSecurityKeyProvider("g", ring);
            PubSubSecurityKey? result = await provider.TryGetKeyAsync(999U);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Constructor_RejectsEmptySecurityGroupId()
        {
            var ring = new PubSubSecurityKeyRing("g");
            Assert.That(
                () => new StaticSecurityKeyProvider(string.Empty, ring),
                Throws.ArgumentException);
        }

        [Test]
        public void Constructor_RejectsNullKeyRing()
        {
            Assert.That(
                () => new StaticSecurityKeyProvider("g", null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void Constructor_RejectsMismatchedSecurityGroupId()
        {
            var ring = new PubSubSecurityKeyRing("group-a");
            Assert.That(
                () => new StaticSecurityKeyProvider("group-b", ring),
                Throws.ArgumentException);
        }

        [Test]
        public void KeyRotated_ForwardsRingRotatedEvents()
        {
            var ring = new PubSubSecurityKeyRing("g");
            var provider = new StaticSecurityKeyProvider("g", ring);
            PubSubKeyRotatedEventArgs? captured = null;
            provider.KeyRotated += (_, e) => captured = e;
            ring.SetCurrent(TestSecurityKeyFactory.Create(11U));
            Assert.Multiple(() =>
            {
                Assert.That(captured, Is.Not.Null);
                Assert.That(captured!.NewTokenId, Is.EqualTo(11U));
            });
        }

        [Test]
        public void GetCurrentKeyAsync_HonorsCancellation()
        {
            var ring = new PubSubSecurityKeyRing("g");
            ring.SetCurrent(TestSecurityKeyFactory.Create(1U));
            var provider = new StaticSecurityKeyProvider("g", ring);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.That(
                async () => await provider.GetCurrentKeyAsync(cts.Token),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void TryGetKeyAsync_HonorsCancellation()
        {
            var ring = new PubSubSecurityKeyRing("g");
            var provider = new StaticSecurityKeyProvider("g", ring);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.That(
                async () => await provider.TryGetKeyAsync(1U, cts.Token),
                Throws.InstanceOf<OperationCanceledException>());
        }
    }
}
