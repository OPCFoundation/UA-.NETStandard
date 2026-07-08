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
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.PubSub.Redundancy.Tests
{
    /// <summary>
    /// Unit tests for the CAS-backed PubSub lease store.
    /// </summary>
    [TestFixture]
    public class SharedStorePubSubLeaseStoreTests
    {
        [Test]
        public async Task TryAcquireAsyncSucceedsWhenFreeAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            var leaseStore = new SharedStorePubSubLeaseStore(store, time);

            PubSubLease? lease = await leaseStore.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration);

            Assert.That(lease, Is.Not.Null);
            Assert.That(lease!.Value.LeaseKey, Is.EqualTo(LeaseKey));
            Assert.That(lease.Value.OwnerId, Is.EqualTo(OwnerA));
            Assert.That(lease.Value.FencingToken, Is.EqualTo(1));
            Assert.That(lease.Value.ExpiresAt, Is.EqualTo(time.GetUtcNow() + LeaseDuration));
        }

        [Test]
        public async Task TryAcquireAsyncRejectsSecondOwnerWhileHeldAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            var leaseStore = new SharedStorePubSubLeaseStore(store, time);

            PubSubLease? first = await leaseStore.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration);
            PubSubLease? second = await leaseStore.TryAcquireAsync(LeaseKey, OwnerB, LeaseDuration);

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Null);
        }

        [Test]
        public async Task TryAcquireAsyncRenewsSameOwnerWithStableTokenAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            var leaseStore = new SharedStorePubSubLeaseStore(store, time);
            PubSubLease? first = await leaseStore.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration);
            time.Advance(TimeSpan.FromSeconds(5));

            PubSubLease? renewed = await leaseStore.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration);

            Assert.That(first, Is.Not.Null);
            Assert.That(renewed, Is.Not.Null);
            Assert.That(renewed!.Value.FencingToken, Is.EqualTo(first!.Value.FencingToken));
            Assert.That(renewed.Value.ExpiresAt, Is.EqualTo(time.GetUtcNow() + LeaseDuration));
        }

        [Test]
        public async Task TryAcquireAsyncAfterExpiryLetsNewOwnerAcquireWithIncrementedTokenAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            var leaseStore = new SharedStorePubSubLeaseStore(store, time);
            PubSubLease? first = await leaseStore.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration);
            time.Advance(LeaseDuration + TimeSpan.FromSeconds(1));

            PubSubLease? takeover = await leaseStore.TryAcquireAsync(LeaseKey, OwnerB, LeaseDuration);

            Assert.That(first, Is.Not.Null);
            Assert.That(takeover, Is.Not.Null);
            Assert.That(takeover!.Value.OwnerId, Is.EqualTo(OwnerB));
            Assert.That(takeover.Value.FencingToken, Is.EqualTo(first!.Value.FencingToken + 1));
        }

        [Test]
        public async Task TryRenewAsyncFailsAfterTakeoverAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            var leaseStore = new SharedStorePubSubLeaseStore(store, time);
            PubSubLease? first = await leaseStore.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration);
            time.Advance(LeaseDuration + TimeSpan.FromSeconds(1));
            PubSubLease? takeover = await leaseStore.TryAcquireAsync(LeaseKey, OwnerB, LeaseDuration);

            PubSubLease? renewed = await leaseStore.TryRenewAsync(first!.Value, LeaseDuration);

            Assert.That(takeover, Is.Not.Null);
            Assert.That(renewed, Is.Null);
        }

        [Test]
        public async Task TryRenewAsyncExtendsLeaseForCurrentOwnerAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            var leaseStore = new SharedStorePubSubLeaseStore(store, time);
            PubSubLease? first = await leaseStore.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration);
            time.Advance(TimeSpan.FromSeconds(5));

            PubSubLease? renewed = await leaseStore.TryRenewAsync(first!.Value, LeaseDuration);

            Assert.That(renewed, Is.Not.Null);
            Assert.That(renewed!.Value.OwnerId, Is.EqualTo(OwnerA));
            Assert.That(renewed.Value.FencingToken, Is.EqualTo(first!.Value.FencingToken));
            Assert.That(renewed.Value.ExpiresAt, Is.EqualTo(time.GetUtcNow() + LeaseDuration));
        }

        [Test]
        public async Task ReleaseAsyncLetsAnotherOwnerAcquireImmediatelyAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            var leaseStore = new SharedStorePubSubLeaseStore(store, time);
            PubSubLease? first = await leaseStore.TryAcquireAsync(LeaseKey, OwnerA, LeaseDuration);

            await leaseStore.ReleaseAsync(first!.Value);
            PubSubLease? next = await leaseStore.TryAcquireAsync(LeaseKey, OwnerB, LeaseDuration);

            Assert.That(next, Is.Not.Null);
            Assert.That(next!.Value.OwnerId, Is.EqualTo(OwnerB));
            Assert.That(next.Value.FencingToken, Is.EqualTo(first.Value.FencingToken + 1));
        }

        private const string LeaseKey = "writer-group";
        private const string OwnerA = "owner-a";
        private const string OwnerB = "owner-b";
        private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    }
}
