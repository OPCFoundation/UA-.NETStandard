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
using NUnit.Framework;
using Opc.Ua.PubSub.Security;

namespace Opc.Ua.PubSub.Tests.Security
{
    /// <summary>
    /// Tests for <see cref="PubSubSecurityKeyRing"/>.
    /// </summary>
    [TestFixture]
    public class PubSubSecurityKeyRingTests
    {
        private static readonly uint[] s_expectedKnownTokens = new uint[] { 1U, 2U, 3U };

        [Test]
        public void Constructor_RejectsEmptySecurityGroupId()
        {
            Assert.That(
                () => new PubSubSecurityKeyRing(string.Empty),
                Throws.ArgumentException);
            Assert.That(
                () => new PubSubSecurityKeyRing(null!),
                Throws.ArgumentException);
        }

        [Test]
        public void Constructor_RejectsNegativePastKeyLimit()
        {
            Assert.That(
                () => new PubSubSecurityKeyRing("g", pastKeyLimit: -1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Constructor_PreservesSecurityGroupId()
        {
            var ring = new PubSubSecurityKeyRing("group-1");
            Assert.That(ring.SecurityGroupId, Is.EqualTo("group-1"));
        }

        [Test]
        public void SetCurrent_FiresRotatedEvent()
        {
            var ring = new PubSubSecurityKeyRing("g");
            PubSubKeyRotatedEventArgs? captured = null;
            ring.Rotated += (_, e) => captured = e;
            PubSubSecurityKey key = TestSecurityKeyFactory.Create(1U);
            ring.SetCurrent(key);
            Assert.Multiple(() =>
            {
                Assert.That(captured, Is.Not.Null);
                Assert.That(captured!.NewTokenId, Is.EqualTo(1U));
                Assert.That(captured.PreviousTokenId, Is.Null);
                Assert.That(ring.Current, Is.SameAs(key));
            });
        }

        [Test]
        public void SetCurrent_DemotesPreviousToPast()
        {
            var ring = new PubSubSecurityKeyRing("g");
            PubSubSecurityKey first = TestSecurityKeyFactory.Create(1U);
            PubSubSecurityKey second = TestSecurityKeyFactory.Create(2U);
            ring.SetCurrent(first);
            ring.SetCurrent(second);
            Assert.Multiple(() =>
            {
                Assert.That(ring.Current, Is.SameAs(second));
                Assert.That(ring.TryGetByTokenId(1U), Is.SameAs(first));
                Assert.That(ring.TryGetByTokenId(2U), Is.SameAs(second));
            });
        }

        [Test]
        public void RotateToNextFuture_PromotesQueuedKey()
        {
            var ring = new PubSubSecurityKeyRing("g");
            PubSubSecurityKey first = TestSecurityKeyFactory.Create(1U);
            PubSubSecurityKey future = TestSecurityKeyFactory.Create(2U);
            ring.SetCurrent(first);
            ring.AddFuture(future);
            uint? capturedPrevious = null;
            uint? capturedNew = null;
            ring.Rotated += (_, e) =>
            {
                capturedPrevious = e.PreviousTokenId;
                capturedNew = e.NewTokenId;
            };
            Assert.Multiple(() =>
            {
                Assert.That(ring.RotateToNextFuture(), Is.True);
                Assert.That(ring.Current, Is.SameAs(future));
                Assert.That(capturedPrevious, Is.EqualTo(1U));
                Assert.That(capturedNew, Is.EqualTo(2U));
            });
        }

        [Test]
        public void RotateToNextFuture_ReturnsFalseWhenQueueEmpty()
        {
            var ring = new PubSubSecurityKeyRing("g");
            Assert.That(ring.RotateToNextFuture(), Is.False);
        }

        [Test]
        public void TryGetByTokenId_FindsCurrentPastAndFuture()
        {
            var ring = new PubSubSecurityKeyRing("g");
            PubSubSecurityKey past = TestSecurityKeyFactory.Create(1U);
            PubSubSecurityKey current = TestSecurityKeyFactory.Create(2U);
            PubSubSecurityKey future = TestSecurityKeyFactory.Create(3U);
            ring.SetCurrent(past);
            ring.SetCurrent(current);
            ring.AddFuture(future);
            Assert.Multiple(() =>
            {
                Assert.That(ring.TryGetByTokenId(1U), Is.SameAs(past));
                Assert.That(ring.TryGetByTokenId(2U), Is.SameAs(current));
                Assert.That(ring.TryGetByTokenId(3U), Is.SameAs(future));
                Assert.That(ring.TryGetByTokenId(99U), Is.Null);
            });
        }

        [Test]
        public void KnownTokenIds_IncludesAllRetainedTokens()
        {
            var ring = new PubSubSecurityKeyRing("g");
            ring.SetCurrent(TestSecurityKeyFactory.Create(1U));
            ring.SetCurrent(TestSecurityKeyFactory.Create(2U));
            ring.AddFuture(TestSecurityKeyFactory.Create(3U));
            Assert.That(ring.KnownTokenIds, Is.EquivalentTo(s_expectedKnownTokens));
        }

        [Test]
        public void PastKeyLimit_EvictsOldestPastKey()
        {
            var ring = new PubSubSecurityKeyRing("g", pastKeyLimit: 2);
            ring.SetCurrent(TestSecurityKeyFactory.Create(1U));
            ring.SetCurrent(TestSecurityKeyFactory.Create(2U));
            ring.SetCurrent(TestSecurityKeyFactory.Create(3U));
            ring.SetCurrent(TestSecurityKeyFactory.Create(4U));
            // After: past = {2,3}, current = 4; token 1 is evicted.
            Assert.Multiple(() =>
            {
                Assert.That(ring.TryGetByTokenId(1U), Is.Null);
                Assert.That(ring.TryGetByTokenId(2U), Is.Not.Null);
                Assert.That(ring.TryGetByTokenId(3U), Is.Not.Null);
                Assert.That(ring.TryGetByTokenId(4U), Is.Not.Null);
            });
        }

        [Test]
        public void SetCurrent_RejectsNull()
        {
            var ring = new PubSubSecurityKeyRing("g");
            Assert.That(() => ring.SetCurrent(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void AddFuture_RejectsNull()
        {
            var ring = new PubSubSecurityKeyRing("g");
            Assert.That(() => ring.AddFuture(null!), Throws.ArgumentNullException);
        }
    }
}
