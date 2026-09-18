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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Core.Tests.Redundancy
{
    /// <summary>
    /// Covers lease authority at expiry boundaries and rejection of delayed or superseded store replies.
    /// </summary>
    [TestFixture]
    [Category("Redundancy")]
    [Parallelizable(ParallelScope.All)]
    public sealed class SharedStoreLeaseExpiryRegressionTests
    {
        /// <summary>
        /// Verifies a failed renewal preserves only the last confirmed lease and permits standby takeover at expiry.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LeaderLosesAuthorityWhenRenewalFailsPastExpiryAsync(bool failCompareAndSwap)
        {
            var time = new FakeTimeProvider();
            using var backend = new InMemorySharedKeyValueStore();
            Mock<ISharedKeyValueStore> store = CreateStore(backend);
            await using SharedStoreLeaseElection leader = CreateElection(store.Object, "A", time);
            await using SharedStoreLeaseElection standby = CreateElection(backend, "B", time);
            var transitions = new ConcurrentQueue<bool>();
            leader.LeadershipChanged += transitions.Enqueue;

            Assert.That(await leader.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            time.Advance(s_renewInterval);
            var failure = new InvalidOperationException("The shared store is unavailable.");
            if (failCompareAndSwap)
            {
                store
                    .Setup(s => s.CompareAndSwapAsync(
                        kLeaseKey, It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                    .Throws(failure);
            }
            else
            {
                store
                    .Setup(s => s.TryGetAsync(kLeaseKey, It.IsAny<CancellationToken>()))
                    .Throws(failure);
            }

            Assert.That(
                async () => await leader.TryAcquireOrRenewAsync().ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(failure.Message));

            time.Advance(s_leaseDuration - s_renewInterval - s_tick);
            Assert.That(leader.IsLeader, Is.True, "The last confirmed lease has not expired.");
            Assert.That(await standby.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.False);
            Assert.That(transitions, Is.EqualTo(s_acquired));

            time.Advance(s_tick);
            Assert.That(transitions, Is.EqualTo(s_acquiredThenLost),
                "Expiry must notify consumers without another read or renewal.");
            Assert.That(leader.IsLeader, Is.False, "Authority ends at the confirmed deadline, not after it.");
            Assert.That(await standby.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            Assert.That(standby.IsLeader, Is.True);

            time.Advance(s_tick);
            Assert.That(leader.IsLeader, Is.False);
            Assert.That(transitions, Is.EqualTo(s_acquiredThenLost));
            ConfigureStore(store, backend);
            Assert.That(await leader.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.False);

            time.Advance(s_leaseDuration);
            Assert.That(await leader.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            Assert.That(leader.IsLeader, Is.True);
            Assert.That(standby.IsLeader, Is.False);
            Assert.That(transitions, Is.EqualTo(s_reacquired));
        }

        /// <summary>
        /// Verifies blocked renewal reads or writes cannot postpone expiry or restore authority with a late reply.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task UnconfirmedRenewalCannotExtendOrResurrectAuthorityAsync(bool blockCompareAndSwap)
        {
            var time = new FakeTimeProvider();
            using var backend = new InMemorySharedKeyValueStore();
            Mock<ISharedKeyValueStore> store = CreateStore(backend);
            await using SharedStoreLeaseElection election = CreateElection(store.Object, "A", time);
            var transitions = new ConcurrentQueue<bool>();
            election.LeadershipChanged += transitions.Enqueue;
            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            time.Advance(s_renewInterval);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var complete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (blockCompareAndSwap)
            {
                store
                    .Setup(s => s.CompareAndSwapAsync(
                        kLeaseKey, It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                    .Returns(async (string key, ByteString expected, ByteString value, CancellationToken ct) =>
                    {
                        bool swapped = await backend.CompareAndSwapAsync(key, expected, value, ct)
                            .ConfigureAwait(false);
                        entered.TrySetResult(true);
                        await complete.Task.ConfigureAwait(false);
                        return swapped;
                    });
            }
            else
            {
                store
                    .Setup(s => s.TryGetAsync(kLeaseKey, It.IsAny<CancellationToken>()))
                    .Returns(async (string key, CancellationToken ct) =>
                    {
                        (bool found, ByteString value) = await backend.TryGetAsync(key, ct).ConfigureAwait(false);
                        entered.TrySetResult(true);
                        await complete.Task.ConfigureAwait(false);
                        return (found, value);
                    });
            }

            Task<bool> renewal = election.TryAcquireOrRenewAsync().AsTask();
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(renewal.IsCompleted, Is.False);
                time.Advance(s_leaseDuration - s_renewInterval - s_tick);
                Assert.That(election.IsLeader, Is.True);
                Assert.That(transitions, Is.EqualTo(s_acquired));

                time.Advance(s_tick);
                Assert.That(transitions, Is.EqualTo(s_acquiredThenLost));
                Assert.That(election.IsLeader, Is.False);
                Assert.That(renewal.IsCompleted, Is.False);

                time.Advance(s_tick);
                complete.TrySetResult(true);
                Assert.That(await renewal.WaitAsync(s_timeout).ConfigureAwait(false), Is.False);
                Assert.That(election.IsLeader, Is.False, "A reply to the expired operation cannot restore authority.");
                Assert.That(transitions, Is.EqualTo(s_acquiredThenLost));

                ConfigureStore(store, backend);
                Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
                Assert.That(election.IsLeader, Is.True);
                Assert.That(transitions, Is.EqualTo(s_reacquired));
            }
            finally
            {
                complete.TrySetResult(true);
                await renewal.WaitAsync(s_timeout).ConfigureAwait(false);
                ConfigureStore(store, backend);
            }
        }

        /// <summary>
        /// Verifies initial acquisition confirmed at or after its stored expiry never grants local leadership.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        public async Task LateInitialConfirmationCannotAcquireExpiredAuthorityAsync(int ticksAfterExpiry)
        {
            var time = new FakeTimeProvider();
            using var backend = new InMemorySharedKeyValueStore();
            Mock<ISharedKeyValueStore> store = CreateStore(backend);
            await using SharedStoreLeaseElection election = CreateElection(store.Object, "A", time);
            var transitions = new ConcurrentQueue<bool>();
            election.LeadershipChanged += transitions.Enqueue;
            var complete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            store
                .Setup(s => s.CompareAndSwapAsync(
                    kLeaseKey, It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(async (string key, ByteString expected, ByteString value, CancellationToken ct) =>
                {
                    bool swapped = await backend.CompareAndSwapAsync(key, expected, value, ct).ConfigureAwait(false);
                    await complete.Task.ConfigureAwait(false);
                    return swapped;
                });

            Task<bool> acquisition = election.TryAcquireOrRenewAsync().AsTask();
            try
            {
                Assert.That(acquisition.IsCompleted, Is.False);
                Assert.That(election.IsLeader, Is.False);
                time.Advance(s_leaseDuration + TimeSpan.FromTicks(ticksAfterExpiry));
                complete.TrySetResult(true);
                Assert.That(await acquisition.WaitAsync(s_timeout).ConfigureAwait(false), Is.False);
                Assert.That(election.IsLeader, Is.False);
                Assert.That(transitions, Is.Empty);
            }
            finally
            {
                complete.TrySetResult(true);
                await acquisition.WaitAsync(s_timeout).ConfigureAwait(false);
                ConfigureStore(store, backend);
            }
        }

        /// <summary>
        /// Verifies a delayed read from an expired attempt cannot revoke a subsequently acquired lease.
        /// </summary>
        [Test]
        public async Task StaleReadCannotRevokeNewlyConfirmedAuthorityAsync()
        {
            var time = new FakeTimeProvider();
            using var backend = new InMemorySharedKeyValueStore();
            Mock<ISharedKeyValueStore> store = CreateStore(backend);
            await using SharedStoreLeaseElection election = CreateElection(store.Object, "A", time);
            var transitions = new ConcurrentQueue<bool>();
            election.LeadershipChanged += transitions.Enqueue;
            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            time.Advance(s_renewInterval);
            var complete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            store
                .Setup(s => s.TryGetAsync(kLeaseKey, It.IsAny<CancellationToken>()))
                .Returns(async (string key, CancellationToken ct) =>
                {
                    (bool found, ByteString value) = await backend.TryGetAsync(key, ct).ConfigureAwait(false);
                    await complete.Task.ConfigureAwait(false);
                    return (found, value);
                });
            Task<bool> stale = election.TryAcquireOrRenewAsync().AsTask();
            try
            {
                Assert.That(stale.IsCompleted, Is.False);
                time.Advance(s_leaseDuration - s_renewInterval);
                ConfigureStore(store, backend);
                Assert.That(
                    await election.TryAcquireOrRenewAsync().AsTask().WaitAsync(s_timeout).ConfigureAwait(false),
                    Is.True);
                Assert.That(transitions, Is.EqualTo(s_reacquired));

                complete.TrySetResult(true);
                Assert.That(await stale.WaitAsync(s_timeout).ConfigureAwait(false), Is.False);
                Assert.That(election.IsLeader, Is.True, "An obsolete result must not change the new lease.");
                Assert.That(transitions, Is.EqualTo(s_reacquired));
                time.Advance(s_leaseDuration - s_tick);
                Assert.That(election.IsLeader, Is.True);
                time.Advance(s_tick);
                Assert.That(election.IsLeader, Is.False);
            }
            finally
            {
                complete.TrySetResult(true);
                await stale.WaitAsync(s_timeout).ConfigureAwait(false);
                ConfigureStore(store, backend);
            }
        }

        /// <summary>
        /// Verifies store reply latency does not extend the deadline encoded when renewal was attempted.
        /// </summary>
        [Test]
        public async Task ConfirmedRenewalExpiresFromTheWriteAttemptNotTheReplyAsync()
        {
            var time = new FakeTimeProvider();
            using var backend = new InMemorySharedKeyValueStore();
            Mock<ISharedKeyValueStore> store = CreateStore(backend);
            await using SharedStoreLeaseElection election = CreateElection(store.Object, "A", time);
            var transitions = new ConcurrentQueue<bool>();
            election.LeadershipChanged += transitions.Enqueue;
            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            time.Advance(s_renewInterval);
            store
                .Setup(s => s.CompareAndSwapAsync(
                    kLeaseKey, It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns((string key, ByteString expected, ByteString value, CancellationToken ct) =>
                {
                    time.Advance(TimeSpan.FromSeconds(5));
                    return backend.CompareAndSwapAsync(key, expected, value, ct);
                });

            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            time.Advance(TimeSpan.FromSeconds(25) - s_tick);
            Assert.That(election.IsLeader, Is.True);
            Assert.That(transitions, Is.EqualTo(s_acquired));
            time.Advance(s_tick);
            Assert.That(transitions, Is.EqualTo(s_acquiredThenLost));
            Assert.That(election.IsLeader, Is.False, "The confirmed stored lease expires at 40s, not 45s.");
            time.Advance(s_tick);
            Assert.That(election.IsLeader, Is.False);
        }

        /// <summary>
        /// Creates an election with shared test lease timing and the supplied replica identity and clock.
        /// </summary>
        private static SharedStoreLeaseElection CreateElection(
            ISharedKeyValueStore store,
            string nodeId,
            TimeProvider time)
        {
            return new SharedStoreLeaseElection(
                store, kLeaseKey, nodeId, s_leaseDuration, s_renewInterval, time);
        }

        /// <summary>
        /// Wraps the in-memory backend in a strict mock whose read and write replies can be delayed or faulted.
        /// </summary>
        private static Mock<ISharedKeyValueStore> CreateStore(InMemorySharedKeyValueStore backend)
        {
            var store = new Mock<ISharedKeyValueStore>(MockBehavior.Strict);
            ConfigureStore(store, backend);
            return store;
        }

        /// <summary>
        /// Restores normal read, compare-and-swap, and delete forwarding after an injected store failure or delay.
        /// </summary>
        private static void ConfigureStore(
            Mock<ISharedKeyValueStore> store,
            InMemorySharedKeyValueStore backend)
        {
            store
                .Setup(s => s.TryGetAsync(kLeaseKey, It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => backend.TryGetAsync(key, ct));
            store
                .Setup(s => s.CompareAndSwapAsync(
                    kLeaseKey, It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns((string key, ByteString expected, ByteString value, CancellationToken ct) =>
                    backend.CompareAndSwapAsync(key, expected, value, ct));
            store
                .Setup(s => s.DeleteAsync(kLeaseKey, It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => backend.DeleteAsync(key, ct));
        }

        /// <summary>
        /// Identifies the shared lease record used by competing test replicas.
        /// </summary>
        private const string kLeaseKey = "lease/expiry-regression";

        /// <summary>
        /// Defines the validity window of each successfully written lease.
        /// </summary>
        private static readonly TimeSpan s_leaseDuration = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Advances the clock to the next renewal attempt within a valid lease.
        /// </summary>
        private static readonly TimeSpan s_renewInterval = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Separates assertions immediately before, at, and after the expiry boundary.
        /// </summary>
        private static readonly TimeSpan s_tick = TimeSpan.FromTicks(1);

        /// <summary>
        /// Bounds waits for controlled store operations to complete.
        /// </summary>
        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Describes the expected notification sequence after initial acquisition.
        /// </summary>
        private static readonly bool[] s_acquired = [true];

        /// <summary>
        /// Describes acquisition followed by expiry of local authority.
        /// </summary>
        private static readonly bool[] s_acquiredThenLost = [true, false];

        /// <summary>
        /// Describes acquisition, expiry, and a later successful acquisition.
        /// </summary>
        private static readonly bool[] s_reacquired = [true, false, true];
    }
}
