// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
            PubSubSecurityKey result = await provider.GetCurrentKeyAsync().ConfigureAwait(false);
            Assert.That(result, Is.SameAs(key));
        }

        [Test]
        public void GetCurrentKeyAsync_ThrowsWhenRingEmpty()
        {
            var ring = new PubSubSecurityKeyRing("g");
            var provider = new StaticSecurityKeyProvider("g", ring);
            Assert.That(
                async () => await provider.GetCurrentKeyAsync().ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task TryGetKeyAsync_ReturnsKeyForKnownToken()
        {
            var ring = new PubSubSecurityKeyRing("g");
            PubSubSecurityKey key = TestSecurityKeyFactory.Create(42U);
            ring.SetCurrent(key);
            var provider = new StaticSecurityKeyProvider("g", ring);
            PubSubSecurityKey? result = await provider.TryGetKeyAsync(42U).ConfigureAwait(false);
            Assert.That(result, Is.SameAs(key));
        }

        [Test]
        public async Task TryGetKeyAsync_ReturnsNullForUnknownToken()
        {
            var ring = new PubSubSecurityKeyRing("g");
            var provider = new StaticSecurityKeyProvider("g", ring);
            PubSubSecurityKey? result = await provider.TryGetKeyAsync(999U).ConfigureAwait(false);
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
                async () => await provider.GetCurrentKeyAsync(cts.Token).ConfigureAwait(false),
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
                async () => await provider.TryGetKeyAsync(1U, cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }
    }
}
