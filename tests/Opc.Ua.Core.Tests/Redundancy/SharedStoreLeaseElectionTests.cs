/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Core.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for the lease-based <see cref="SharedStoreLeaseElection"/>.
    /// </summary>
    [TestFixture]
    [Category("Redundancy")]
    [Parallelizable(ParallelScope.All)]
    public sealed class SharedStoreLeaseElectionTests
    {
        private const string LeaseKey = "lease/address-space";
        private static readonly TimeSpan s_leaseDuration = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan s_renewInterval = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(5);
        private static readonly bool[] s_acquired = [true];
        private static readonly bool[] s_acquireThenLoss = [true, false];

        [Test]
        public void ConstructorWithNullStoreThrows()
        {
            var time = new FakeTimeProvider();

            Assert.That(
                () => new SharedStoreLeaseElection(
                    null!, LeaseKey, "A", s_leaseDuration, s_renewInterval, time),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorWithEmptyLeaseKeyThrows()
        {
            using var store = new InMemorySharedKeyValueStore();
            var time = new FakeTimeProvider();

            Assert.That(
                () => new SharedStoreLeaseElection(
                    store, string.Empty, "A", s_leaseDuration, s_renewInterval, time),
                Throws.ArgumentException);
        }

        [Test]
        public void ConstructorWithNullNodeIdThrows()
        {
            using var store = new InMemorySharedKeyValueStore();
            var time = new FakeTimeProvider();

            Assert.That(
                () => new SharedStoreLeaseElection(
                    store, LeaseKey, null!, s_leaseDuration, s_renewInterval, time),
                Throws.ArgumentException);
        }

        [Test]
        public async Task FirstAcquirerBecomesLeaderAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using SharedStoreLeaseElection election = CreateElection(store, "A", time);

            bool acquired = await election.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(acquired, Is.True);
            Assert.That(election.IsLeader, Is.True);
        }

        [Test]
        public async Task SecondReplicaIsFollowerWhileLeaseHeldAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using SharedStoreLeaseElection a = CreateElection(store, "A", time);
            await using SharedStoreLeaseElection b = CreateElection(store, "B", time);

            Assert.That(await a.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);

            bool acquired = await b.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(acquired, Is.False);
            Assert.That(b.IsLeader, Is.False);
        }

        [Test]
        public async Task LeaderRenewsOwnLeaseAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using SharedStoreLeaseElection a = CreateElection(store, "A", time);

            Assert.That(await a.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            time.Advance(TimeSpan.FromSeconds(10));

            bool renewed = await a.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(renewed, Is.True);
            Assert.That(a.IsLeader, Is.True);
        }

        [Test]
        public async Task StandbyTakesOverAfterLeaseExpiresAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using SharedStoreLeaseElection a = CreateElection(store, "A", time);
            await using SharedStoreLeaseElection b = CreateElection(store, "B", time);

            Assert.That(await a.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            time.Advance(s_leaseDuration + TimeSpan.FromSeconds(1));

            Assert.That(await b.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            Assert.That(await a.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.False);
            Assert.That(b.IsLeader, Is.True);
            Assert.That(a.IsLeader, Is.False);
        }

        [Test]
        public async Task LeadershipChangedReportsAcquireThenLossAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using SharedStoreLeaseElection a = CreateElection(store, "A", time);
            await using SharedStoreLeaseElection b = CreateElection(store, "B", time);

            var transitions = new List<bool>();
            a.LeadershipChanged += transitions.Add;

            await a.TryAcquireOrRenewAsync().ConfigureAwait(false);
            time.Advance(s_leaseDuration + TimeSpan.FromSeconds(1));
            await b.TryAcquireOrRenewAsync().ConfigureAwait(false);
            await a.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(transitions, Is.EqualTo(s_acquireThenLoss));
        }

        [Test]
        public async Task AcquireReturnsFalseWhenCompareAndSwapLosesRaceAsync()
        {
            var time = new FakeTimeProvider();
            var store = new Mock<ISharedKeyValueStore>();
            store
                .Setup(s => s.TryGetAsync(LeaseKey, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<(bool Found, ByteString Value)>((false, default)));
            store
                .Setup(s => s.CompareAndSwapAsync(
                    LeaseKey,
                    It.IsAny<ByteString>(),
                    It.IsAny<ByteString>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(false));

            await using SharedStoreLeaseElection election = CreateElection(store.Object, "A", time);

            bool acquired = await election.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(acquired, Is.False);
            Assert.That(election.IsLeader, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ConcurrentAttemptsConfirmOwnedLeaseAsync(bool blockRead)
        {
            var time = new FakeTimeProvider();
            using var backend = new InMemorySharedKeyValueStore();
            var firstReplyBlocked = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirstReply = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int reads = 0;
            int swaps = 0;
            var store = new Mock<ISharedKeyValueStore>();
            store
                .Setup(s => s.TryGetAsync(LeaseKey, It.IsAny<CancellationToken>()))
                .Returns(async (string key, CancellationToken ct) =>
                {
                    (bool found, ByteString value) = await backend.TryGetAsync(key, ct).ConfigureAwait(false);
                    if (blockRead && Interlocked.Increment(ref reads) == 1)
                    {
                        firstReplyBlocked.TrySetResult(true);
                        await releaseFirstReply.Task.WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
                    }
                    return (found, value);
                });
            store
                .Setup(s => s.CompareAndSwapAsync(
                    LeaseKey,
                    It.IsAny<ByteString>(),
                    It.IsAny<ByteString>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (string key, ByteString expected, ByteString value, CancellationToken ct) =>
                {
                    bool swapped = await backend.CompareAndSwapAsync(key, expected, value, ct).ConfigureAwait(false);
                    if (!blockRead && Interlocked.Increment(ref swaps) == 1)
                    {
                        firstReplyBlocked.TrySetResult(true);
                        await releaseFirstReply.Task.WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
                    }
                    return swapped;
                });

            await using SharedStoreLeaseElection election = CreateElection(store.Object, "A", time);
            var transitions = new List<bool>();
            election.LeadershipChanged += transitions.Add;
            Task<bool> first = election.TryAcquireOrRenewAsync().AsTask();
            try
            {
                await firstReplyBlocked.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                time.Advance(s_renewInterval);
                Assert.That(
                    await election.TryAcquireOrRenewAsync().AsTask().WaitAsync(s_timeout).ConfigureAwait(false),
                    Is.True);
                Assert.That(first.IsCompleted, Is.False, "The newer attempt must not wait for the old store reply.");
                releaseFirstReply.TrySetResult(true);
                Assert.That(await first.WaitAsync(s_timeout).ConfigureAwait(false), Is.True,
                    "Overlapping calls must both confirm the unexpired lease owned by this replica.");
                Assert.That(election.IsLeader, Is.True);
                Assert.That(transitions, Is.EqualTo(s_acquired));
                time.Advance(s_leaseDuration - TimeSpan.FromTicks(1));
                Assert.That(election.IsLeader, Is.True, "An older reply must not shorten the confirmed renewal.");
                time.Advance(TimeSpan.FromTicks(1));
                Assert.That(election.IsLeader, Is.False);
                Assert.That(transitions, Is.EqualTo(s_acquireThenLoss));
            }
            finally
            {
                releaseFirstReply.TrySetResult(true);
                await first.WaitAsync(s_timeout).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A foreign-owner verdict sampled before a newer acquisition cannot revoke that acquisition.
        /// </summary>
        [Test]
        public async Task StaleForeignLeaseReadDoesNotRevokeNewerAcquisitionAsync()
        {
            var clock = new FakeTimeProvider();
            using var backend = new InMemorySharedKeyValueStore();
            await using SharedStoreLeaseElection other = CreateElection(backend, "B", clock);
            Assert.That(await other.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            var time = new Mock<TimeProvider>();
            time.Setup(value => value.GetUtcNow()).Returns(clock.GetUtcNow);
            time.Setup(value => value.GetTimestamp()).Returns(clock.GetTimestamp);
            time.SetupGet(value => value.TimestampFrequency).Returns(clock.TimestampFrequency);
            time.Setup(value => value.CreateTimer(
                    It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
                .Returns((TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
                    clock.CreateTimer(callback, state, dueTime, period));
            await using SharedStoreLeaseElection election = CreateElection(backend, "A", time.Object);
            var transitions = new List<bool>();
            election.LeadershipChanged += transitions.Add;
            Task<bool>? newerAttempt = null;
            bool interleave = true;
            time.Setup(value => value.GetUtcNow()).Returns(() =>
            {
                DateTimeOffset observed = clock.GetUtcNow();
                if (interleave)
                {
                    interleave = false;
                    clock.Advance(s_leaseDuration);
                    newerAttempt = election.TryAcquireOrRenewAsync().AsTask();
                    Assert.That(newerAttempt.IsCompleted, Is.True,
                        "The in-memory store must complete the newer acquisition before the old verdict.");
                }
                return observed;
            });

            bool result = await election.TryAcquireOrRenewAsync().AsTask().WaitAsync(s_timeout).ConfigureAwait(false);

            Assert.That(newerAttempt, Is.Not.Null);
            Assert.That(await newerAttempt!.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(election.IsLeader, Is.True, "The stale failed read must not revoke the newer lease.");
                Assert.That(result, Is.True);
                Assert.That(transitions, Is.EqualTo(s_acquired));
                Assert.That(other.IsLeader, Is.False);
            }
            clock.Advance(s_leaseDuration - TimeSpan.FromTicks(1));
            Assert.That(election.IsLeader, Is.True);
            clock.Advance(TimeSpan.FromTicks(1));
            Assert.That(election.IsLeader, Is.False);
            Assert.That(transitions, Is.EqualTo(s_acquireThenLoss));
        }

        /// <summary>
        /// A held failed-CAS revalidation cannot revoke an acquisition completed after the foreign lease expires.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task StaleFailedRevalidationDoesNotRevokeNewerAcquisitionAsync(bool failedAttemptStartsLast)
        {
            var time = new FakeTimeProvider();
            using var backend = new InMemorySharedKeyValueStore();
            await using SharedStoreLeaseElection other = CreateElection(backend, "B", time);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var acquisitionEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseAcquisition = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int reads = 0;
            int swaps = 0;
            var store = new Mock<ISharedKeyValueStore>();
            store.Setup(value => value.TryGetAsync(LeaseKey, It.IsAny<CancellationToken>()))
                .Returns(async (string key, CancellationToken ct) =>
                {
                    int read = Interlocked.Increment(ref reads);
                    if (failedAttemptStartsLast && read == 1)
                    {
                        acquisitionEntered.TrySetResult(true);
                        await releaseAcquisition.Task.WaitAsync(s_timeout, ct).ConfigureAwait(false);
                    }
                    (bool found, ByteString value) = await backend.TryGetAsync(key, ct).ConfigureAwait(false);
                    if (read == (failedAttemptStartsLast ? 3 : 2))
                    {
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(s_timeout, ct).ConfigureAwait(false);
                    }
                    return (found, value);
                });
            store.Setup(value => value.CompareAndSwapAsync(
                    LeaseKey, It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(async (string key, ByteString expected, ByteString value, CancellationToken ct) =>
                {
                    if (Interlocked.Increment(ref swaps) == 1)
                    {
                        Assert.That(await other.TryAcquireOrRenewAsync(ct).ConfigureAwait(false), Is.True);
                    }
                    return await backend.CompareAndSwapAsync(key, expected, value, ct).ConfigureAwait(false);
                });
            await using SharedStoreLeaseElection election = CreateElection(store.Object, "A", time);
            var transitions = new List<bool>();
            election.LeadershipChanged += transitions.Add;
            Task<bool>? stale = null;
            Task<bool>? acquisition = null;
            try
            {
                if (failedAttemptStartsLast)
                {
                    acquisition = election.TryAcquireOrRenewAsync().AsTask();
                    await acquisitionEntered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                }
                stale = election.TryAcquireOrRenewAsync().AsTask();
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(other.IsLeader, Is.True);
                time.Advance(s_leaseDuration);
                if (failedAttemptStartsLast)
                {
                    releaseAcquisition.TrySetResult(true);
                }
                else
                {
                    acquisition = election.TryAcquireOrRenewAsync().AsTask();
                }
                Assert.That(await acquisition!.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
                Assert.That(stale.IsCompleted, Is.False);

                release.TrySetResult(true);
                bool result = await stale.WaitAsync(s_timeout).ConfigureAwait(false);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(election.IsLeader, Is.True, "The old revalidation must not revoke the newer lease.");
                    Assert.That(result, Is.True);
                    Assert.That(transitions, Is.EqualTo(s_acquired));
                    Assert.That(other.IsLeader, Is.False);
                    Assert.That(swaps, Is.EqualTo(2));
                }
                time.Advance(s_leaseDuration - TimeSpan.FromTicks(1));
                Assert.That(election.IsLeader, Is.True);
                time.Advance(TimeSpan.FromTicks(1));
                Assert.That(election.IsLeader, Is.False);
                Assert.That(transitions, Is.EqualTo(s_acquireThenLoss));
            }
            finally
            {
                release.TrySetResult(true);
                releaseAcquisition.TrySetResult(true);
                if (stale != null)
                {
                    await stale.WaitAsync(s_timeout).ConfigureAwait(false);
                }
                if (acquisition != null)
                {
                    await acquisition.WaitAsync(s_timeout).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// A current foreign-owner observation must revoke leadership when no later confirmation supersedes it.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task FreshForeignLeaseObservationRevokesLeadershipAsync(
            bool loseCompareAndSwap,
            bool confirmBeforeRevalidation)
        {
            var time = new FakeTimeProvider();
            using var backend = new InMemorySharedKeyValueStore();
            await using SharedStoreLeaseElection other = CreateElection(backend, "B", time);
            var store = new Mock<ISharedKeyValueStore>();
            store.Setup(value => value.TryGetAsync(LeaseKey, It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => backend.TryGetAsync(key, ct));
            store.Setup(value => value.CompareAndSwapAsync(
                    LeaseKey, It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns((string key, ByteString expected, ByteString value, CancellationToken ct) =>
                    backend.CompareAndSwapAsync(key, expected, value, ct));
            await using SharedStoreLeaseElection election = CreateElection(store.Object, "A", time);
            var transitions = new List<bool>();
            election.LeadershipChanged += transitions.Add;
            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            time.Advance(s_renewInterval);
            int replacements = 0;
            if (loseCompareAndSwap)
            {
                store.Setup(value => value.CompareAndSwapAsync(
                        LeaseKey, It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                    .Returns(async (string key, ByteString expected, ByteString value, CancellationToken ct) =>
                    {
                        if (Interlocked.Increment(ref replacements) == 1)
                        {
                            if (confirmBeforeRevalidation)
                            {
                                Assert.That(await election.TryAcquireOrRenewAsync(ct).ConfigureAwait(false), Is.True);
                            }
                            (bool found, ByteString current) = await backend.TryGetAsync(key, ct)
                                .ConfigureAwait(false);
                            Assert.That(found, Is.True);
                            Assert.That(await backend.CompareAndSwapAsync(
                                key, current, default, ct).ConfigureAwait(false), Is.True);
                            Assert.That(await other.TryAcquireOrRenewAsync(ct).ConfigureAwait(false), Is.True);
                        }
                        return await backend.CompareAndSwapAsync(key, expected, value, ct).ConfigureAwait(false);
                    });
            }
            else
            {
                (bool found, ByteString current) = await backend.TryGetAsync(LeaseKey).ConfigureAwait(false);
                Assert.That(found, Is.True);
                Assert.That(await backend.CompareAndSwapAsync(
                    LeaseKey, current, default).ConfigureAwait(false), Is.True);
                Assert.That(await other.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            }

            bool result = await election.TryAcquireOrRenewAsync().AsTask().WaitAsync(s_timeout).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Is.False);
                Assert.That(election.IsLeader, Is.False);
                Assert.That(other.IsLeader, Is.True);
                Assert.That(transitions, Is.EqualTo(s_acquireThenLoss));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedCompareAndSwapPreservesUnexpiredLocalLeaseAsync(bool renewedInStore)
        {
            var time = new FakeTimeProvider();
            using var backend = new InMemorySharedKeyValueStore();
            var store = new Mock<ISharedKeyValueStore>();
            store.Setup(s => s.TryGetAsync(LeaseKey, It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => backend.TryGetAsync(key, ct));
            store.Setup(s => s.CompareAndSwapAsync(
                    LeaseKey, It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns((string key, ByteString expected, ByteString value, CancellationToken ct) =>
                    backend.CompareAndSwapAsync(key, expected, value, ct));
            await using SharedStoreLeaseElection election = CreateElection(store.Object, "A", time);
            var transitions = new List<bool>();
            election.LeadershipChanged += transitions.Add;
            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            time.Advance(s_renewInterval);

            store.Setup(s => s.CompareAndSwapAsync(
                    LeaseKey, It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(async (string key, ByteString expected, ByteString value, CancellationToken ct) =>
                {
                    if (renewedInStore)
                    {
                        Assert.That(
                            await backend.CompareAndSwapAsync(key, expected, value, ct).ConfigureAwait(false),
                            Is.True);
                    }
                    return false;
                });

            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            Assert.That(election.IsLeader, Is.True);
            Assert.That(transitions, Is.EqualTo(s_acquired));
            TimeSpan remaining = renewedInStore ? s_leaseDuration : s_leaseDuration - s_renewInterval;
            time.Advance(remaining - TimeSpan.FromTicks(1));
            Assert.That(election.IsLeader, Is.True);
            time.Advance(TimeSpan.FromTicks(1));
            Assert.That(election.IsLeader, Is.False, "A failed CAS cannot invent a later stored expiry.");
            Assert.That(transitions, Is.EqualTo(s_acquireThenLoss));
        }

        [Test]
        public async Task DisposalWakesPendingStoreAttemptsAsync()
        {
            var time = new FakeTimeProvider();
            using var backend = new InMemorySharedKeyValueStore();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int reads = 0;
            var store = new Mock<ISharedKeyValueStore>();
            store.Setup(s => s.TryGetAsync(LeaseKey, It.IsAny<CancellationToken>()))
                .Returns(async (string key, CancellationToken ct) =>
                {
                    int read = Interlocked.Increment(ref reads);
                    if (read is 2 or 3)
                    {
                        if (read == 3)
                        {
                            entered.TrySetResult(true);
                        }
                        await release.Task.WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
                    }
                    return await backend.TryGetAsync(key, ct).ConfigureAwait(false);
                });
            store.Setup(s => s.CompareAndSwapAsync(
                    LeaseKey, It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns((string key, ByteString expected, ByteString value, CancellationToken ct) =>
                    backend.CompareAndSwapAsync(key, expected, value, ct));
            SharedStoreLeaseElection election = CreateElection(store.Object, "A", time);
            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            Task<bool> first = election.TryAcquireOrRenewAsync().AsTask();
            Task<bool> second = election.TryAcquireOrRenewAsync().AsTask();
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                await election.DisposeAsync().AsTask().WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(election.IsLeader, Is.False);
                await Assert.ThatAsync(
                    () => first.WaitAsync(s_timeout),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                await Assert.ThatAsync(
                    () => second.WaitAsync(s_timeout),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(release.Task.IsCompleted, Is.False, "Disposal must not require the provider to reply.");
                Assert.That(
                    async () => await election.TryAcquireOrRenewAsync().ConfigureAwait(false),
                    Throws.TypeOf<ObjectDisposedException>());
            }
            finally
            {
                release.TrySetResult(true);
                await election.DisposeAsync().AsTask().WaitAsync(s_timeout).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task AcquireTakesOverWhenStoredLeaseIsTooShortAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            // Fewer than four bytes: the lease parser rejects it at the length guard.
            await store.SetAsync(LeaseKey, new ByteString(new byte[] { 1, 2 })).ConfigureAwait(false);
            await using SharedStoreLeaseElection election = CreateElection(store, "A", time);

            bool acquired = await election.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(acquired, Is.True);
            Assert.That(election.IsLeader, Is.True);
        }

        [Test]
        public async Task AcquireTakesOverWhenStoredLeaseHasInvalidOwnerLengthAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            // Declares a 16-byte owner but carries no owner/expiry payload, so the
            // parser rejects it on the trailing-length guard.
            await store.SetAsync(LeaseKey, new ByteString(new byte[] { 0x10, 0x00, 0x00, 0x00 })).ConfigureAwait(false);
            await using SharedStoreLeaseElection election = CreateElection(store, "A", time);

            bool acquired = await election.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(acquired, Is.True);
            Assert.That(election.IsLeader, Is.True);
        }

        [Test]
        public async Task ReleaseOnDisposeAllowsImmediateTakeoverAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();

            SharedStoreLeaseElection a = CreateElection(store, "A", time);
            await a.TryAcquireOrRenewAsync().ConfigureAwait(false);
            await a.DisposeAsync().ConfigureAwait(false);

            await using SharedStoreLeaseElection b = CreateElection(store, "B", time);
            bool acquired = await b.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(acquired, Is.True);
        }

        /// <summary>
        /// Verifies that a delayed release cannot delete a newer owner's lease.
        /// </summary>
        [Test]
        public async Task DelayedReleaseCannotDeleteNewerLeaseAsync()
        {
            var time = new FakeTimeProvider();
            using var inner = new InMemorySharedKeyValueStore();
            var releaseReadStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseReadGate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int reads = 0;
            var store = new Mock<ISharedKeyValueStore>();
            store
                .Setup(s => s.TryGetAsync(LeaseKey, It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    if (Interlocked.Increment(ref reads) == 2)
                    {
                        releaseReadStarted.TrySetResult(true);
                        await releaseReadGate.Task.ConfigureAwait(false);
                    }
                    return await inner.TryGetAsync(LeaseKey).ConfigureAwait(false);
                });
            store
                .Setup(s => s.CompareAndSwapAsync(
                    LeaseKey,
                    It.IsAny<ByteString>(),
                    It.IsAny<ByteString>(),
                    It.IsAny<CancellationToken>()))
                .Returns((string key, ByteString expected, ByteString value, CancellationToken ct) =>
                    inner.CompareAndSwapAsync(key, expected, value, ct));

            SharedStoreLeaseElection a = CreateElection(store.Object, "A", time);
            await a.TryAcquireOrRenewAsync().ConfigureAwait(false);
            Task release = a.DisposeAsync().AsTask();
            await releaseReadStarted.Task.WaitAsync(s_timeout).ConfigureAwait(false);

            time.Advance(s_leaseDuration + TimeSpan.FromSeconds(1));
            await using SharedStoreLeaseElection b = CreateElection(store.Object, "B", time);
            Assert.That(await b.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);

            releaseReadGate.TrySetResult(true);
            await release.ConfigureAwait(false);

            await using SharedStoreLeaseElection c = CreateElection(store.Object, "C", time);
            Assert.That(await c.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.False);
            Assert.That(b.IsLeader, Is.True);
        }

        [Test]
        public async Task DisposeAsyncIsIdempotentAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            SharedStoreLeaseElection election = CreateElection(store, "A", time);

            await election.TryAcquireOrRenewAsync().ConfigureAwait(false);
            await election.DisposeAsync().ConfigureAwait(false);

            Assert.That(async () => await election.DisposeAsync().ConfigureAwait(false), Throws.Nothing);
        }

        [Test]
        public async Task StartAcquiresLeadershipThroughRenewLoopAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            SharedStoreLeaseElection election = CreateElection(
                store, "A", time, TimeSpan.FromMilliseconds(20));

            var acquired = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            election.LeadershipChanged += value =>
            {
                if (value)
                {
                    acquired.TrySetResult(true);
                }
            };

            try
            {
                election.Start();
                election.Start();

                await WaitWithTimeoutAsync(acquired.Task, "leadership was not acquired by the renew loop").ConfigureAwait(false);

                Assert.That(election.IsLeader, Is.True);
            }
            finally
            {
                await election.DisposeAsync().ConfigureAwait(false);
            }

            // The owner released its lease on dispose, so the key is gone.
            (bool found, ByteString _) = await store.TryGetAsync(LeaseKey).ConfigureAwait(false);
            Assert.That(found, Is.False);
        }

        [Test]
        public async Task RenewLoopLogsWhenAcquireThrowsAsync()
        {
            var time = new FakeTimeProvider();
            var logger = new RecordingLogger();
            var store = new Mock<ISharedKeyValueStore>();
            store
                .Setup(s => s.TryGetAsync(LeaseKey, It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("store offline"));

            SharedStoreLeaseElection election = CreateElection(
                store.Object, "A", time, TimeSpan.FromMilliseconds(20), logger);

            try
            {
                election.Start();

                await WaitWithTimeoutAsync(logger.ErrorLogged, "renew loop did not log the store failure").ConfigureAwait(false);

                Assert.That(election.IsLeader, Is.False);
            }
            finally
            {
                await election.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task RenewLoopStopsWhenAcquireIsCanceledAsync()
        {
            var time = new FakeTimeProvider();
            var probed = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var store = new Mock<ISharedKeyValueStore>();
            store
                .Setup(s => s.TryGetAsync(LeaseKey, It.IsAny<CancellationToken>()))
                .Callback(() => probed.TrySetResult(true))
                .Throws(new OperationCanceledException());

            SharedStoreLeaseElection election = CreateElection(
                store.Object, "A", time, TimeSpan.FromMilliseconds(20));

            try
            {
                election.Start();

                await WaitWithTimeoutAsync(probed.Task, "renew loop never queried the store").ConfigureAwait(false);

                Assert.That(election.IsLeader, Is.False);
            }
            finally
            {
                await election.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The lease this replica wrote has to still be running for it to count
        /// as the leader. Checked on read, because a store that hangs rather
        /// than throws never reaches the renew failure path at all while every
        /// other replica watches the lease expire.
        /// </summary>
        [Test]
        public async Task IsLeaderReportsFalseOnceTheLocalLeaseHasExpiredAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using SharedStoreLeaseElection election = CreateElection(store, "A", time);

            Assert.That(
                await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            Assert.That(election.IsLeader, Is.True);

            // still inside the lease.
            time.Advance(s_leaseDuration - TimeSpan.FromSeconds(1));
            Assert.That(election.IsLeader, Is.True);

            // past it: another replica is free to take over, so this one is no
            // longer the leader.
            time.Advance(TimeSpan.FromSeconds(2));
            Assert.That(election.IsLeader, Is.False);
        }

        /// <summary>
        /// A renewed lease extends the local expiry, so a leader that keeps
        /// reaching the store keeps leading.
        /// </summary>
        [Test]
        public async Task RenewingTheLeaseExtendsLeadershipAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using SharedStoreLeaseElection election = CreateElection(store, "A", time);

            Assert.That(
                await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);

            time.Advance(s_leaseDuration - TimeSpan.FromSeconds(1));

            Assert.That(
                await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);

            time.Advance(TimeSpan.FromSeconds(2));
            Assert.That(election.IsLeader, Is.True);
        }

        /// <summary>
        /// A renew that never reached the store steps the replica down once the
        /// lease it last wrote has run out, and announces it - otherwise the
        /// standby that took over and this replica would both be leading.
        /// </summary>
        [Test]
        public async Task RenewFailureStepsDownOnceTheLeaseExpiresAsync()
        {
            var time = new FakeTimeProvider();
            int probes = 0;
            var store = new Mock<ISharedKeyValueStore>();

            store
                .Setup(s => s.TryGetAsync(LeaseKey, It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    if (Interlocked.Increment(ref probes) > 1)
                    {
                        throw new InvalidOperationException("store offline");
                    }

                    return new ValueTask<(bool, ByteString)>((false, default));
                });

            store
                .Setup(s => s.CompareAndSwapAsync(
                    LeaseKey,
                    It.IsAny<ByteString>(),
                    It.IsAny<ByteString>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(true));

            SharedStoreLeaseElection election = CreateElection(
                store.Object, "A", time, TimeSpan.FromMilliseconds(20));

            var steppedDown = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var acquired = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            election.LeadershipChanged += value =>
            {
                if (value)
                {
                    acquired.TrySetResult(true);
                }
                else
                {
                    steppedDown.TrySetResult(true);
                }
            };

            try
            {
                election.Start();

                await WaitWithTimeoutAsync(
                    acquired.Task, "leadership was not acquired").ConfigureAwait(false);

                // the store is unreachable from here on; once the lease it last
                // wrote runs out the replica has to give leadership up.
                time.Advance(s_leaseDuration + TimeSpan.FromSeconds(1));

                await WaitWithTimeoutAsync(
                    steppedDown.Task,
                    "the replica kept leading on an expired lease").ConfigureAwait(false);

                Assert.That(election.IsLeader, Is.False);
            }
            finally
            {
                await election.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A store that hangs rather than throws never lets the renew loop come
        /// round to its failure path, so the step-down is driven from a watchdog
        /// the store cannot block. Without it a replica keeps announcing itself
        /// as leader on a lease every other replica has watched expire.
        /// </summary>
        [Test]
        public async Task StoreThatHangsStillStepsTheLeaderDownAsync()
        {
            var time = new FakeTimeProvider();
            var released = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int probes = 0;
            var store = new Mock<ISharedKeyValueStore>();

            store
                .Setup(s => s.TryGetAsync(LeaseKey, It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    if (Interlocked.Increment(ref probes) > 1)
                    {
                        entered.TrySetResult(true);
                        await released.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                    }
                    return (false, default(ByteString));
                });

            store
                .Setup(s => s.CompareAndSwapAsync(
                    LeaseKey,
                    It.IsAny<ByteString>(),
                    It.IsAny<ByteString>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(true));

            SharedStoreLeaseElection election = CreateElection(
                store.Object, "A", time, TimeSpan.FromMilliseconds(20));

            var acquired = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var steppedDown = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            election.LeadershipChanged += value =>
            {
                if (value)
                {
                    acquired.TrySetResult(true);
                }
                else
                {
                    steppedDown.TrySetResult(true);
                }
            };

            try
            {
                election.Start();

                await WaitWithTimeoutAsync(
                    acquired.Task, "leadership was not acquired").ConfigureAwait(false);

                Task<bool> blocked = election.TryAcquireOrRenewAsync().AsTask();
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                time.Advance(s_leaseDuration + TimeSpan.FromSeconds(1));

                await WaitWithTimeoutAsync(
                    steppedDown.Task,
                    "the replica kept leading while the store hung").ConfigureAwait(false);

                Assert.That(election.IsLeader, Is.False);
                released.TrySetResult(true);
                Assert.That(await blocked.WaitAsync(s_timeout).ConfigureAwait(false), Is.False);
            }
            finally
            {
                released.TrySetResult(true);
                await election.DisposeAsync().AsTask().WaitAsync(s_timeout).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ExpiredLeaseNotifiesBeforeBlockedStoreReadAsync()
        {
            var clock = new FakeTimeProvider();
            var timer = new Mock<ITimer>();
            timer.Setup(value => value.Change(It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>())).Returns(true);
            var time = new Mock<TimeProvider>();
            time.Setup(value => value.GetUtcNow()).Returns(clock.GetUtcNow);
            time.Setup(value => value.GetTimestamp()).Returns(clock.GetTimestamp);
            time.SetupGet(value => value.TimestampFrequency).Returns(clock.TimestampFrequency);
            time.Setup(value => value.CreateTimer(
                    It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
                .Returns(timer.Object);
            using var backend = new InMemorySharedKeyValueStore();
            var store = new Mock<ISharedKeyValueStore>();
            store.Setup(value => value.TryGetAsync(LeaseKey, It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => backend.TryGetAsync(key, ct));
            store.Setup(value => value.CompareAndSwapAsync(
                    LeaseKey, It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns((string key, ByteString expected, ByteString value, CancellationToken ct) =>
                    backend.CompareAndSwapAsync(key, expected, value, ct));
            await using SharedStoreLeaseElection election = CreateElection(store.Object, "A", time.Object);
            var transitions = new List<bool>();
            election.LeadershipChanged += transitions.Add;
            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            clock.Advance(s_leaseDuration);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            store.Setup(value => value.TryGetAsync(LeaseKey, It.IsAny<CancellationToken>()))
                .Returns(async (string key, CancellationToken ct) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(s_timeout, CancellationToken.None).ConfigureAwait(false);
                    return await backend.TryGetAsync(key, ct).ConfigureAwait(false);
                });

            Task<bool> attempt = election.TryAcquireOrRenewAsync().AsTask();
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(attempt.IsCompleted, Is.False);
                Assert.That(transitions, Is.EqualTo(s_acquireThenLoss),
                    "An expired lease must be announced before awaiting the shared store.");
            }
            finally
            {
                release.TrySetResult(true);
                await attempt.WaitAsync(s_timeout).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// LeadershipChanged notifications arrive in the order the transitions
        /// were made. A handler that itself causes a transition used to be called
        /// back re-entrantly with the newer value before its own call returned,
        /// so a subscriber saw "not leader" and then "leader" - the reverse of
        /// what happened - and was left believing it still led.
        /// </summary>
        [Test]
        public async Task LeadershipNotificationsArriveInTransitionOrderAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using SharedStoreLeaseElection a = CreateElection(store, "A", time);
            await using SharedStoreLeaseElection b = CreateElection(store, "B", time);

            var observed = new List<bool>();
            bool transitioned = false;

            a.LeadershipChanged += value =>
            {
                if (value && !transitioned)
                {
                    transitioned = true;

                    // A's lease runs out, B takes it over, and A's next renew
                    // finds it held - a transition to false made from inside
                    // the notification for true.
                    time.Advance(s_leaseDuration + TimeSpan.FromSeconds(1));
                    b.TryAcquireOrRenewAsync().AsTask().GetAwaiter().GetResult();
                    a.TryAcquireOrRenewAsync().AsTask().GetAwaiter().GetResult();
                }

                observed.Add(value);
            };

            Assert.That(await a.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);

            Assert.That(observed, Is.EqualTo(s_acquireThenLoss));
            Assert.That(a.IsLeader, Is.False);
            Assert.That(b.IsLeader, Is.True);
        }

        /// <summary>
        /// The lease expiry is computed before the store call. A call that took
        /// longer than the lease duration wrote a lease that has already run
        /// out, so it is not leadership however the swap went.
        /// </summary>
        [Test]
        public async Task AcquireThatOutlastsTheLeaseIsNotLeadershipAsync()
        {
            var time = new FakeTimeProvider();
            var store = new Mock<ISharedKeyValueStore>();

            store
                .Setup(s => s.TryGetAsync(LeaseKey, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<(bool, ByteString)>((false, default)));

            store
                .Setup(s => s.CompareAndSwapAsync(
                    LeaseKey,
                    It.IsAny<ByteString>(),
                    It.IsAny<ByteString>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    // the store took longer than the lease it was writing.
                    time.Advance(s_leaseDuration + TimeSpan.FromSeconds(1));
                    return new ValueTask<bool>(true);
                });

            await using SharedStoreLeaseElection election = CreateElection(
                store.Object, "A", time);

            bool acquired = await election.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(acquired, Is.False);
            Assert.That(election.IsLeader, Is.False);
        }

        /// <summary>
        /// Verifies subscriber failures are logged without preventing acquisition, later subscribers, or lease release.
        /// </summary>
        [Test]
        public async Task ThrowingLeadershipSubscriberDoesNotFailAcquireOrDisposeAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            var logger = new RecordingLogger();
            await using SharedStoreLeaseElection election = CreateElection(store, "A", time, logger: logger);
            var observed = new List<bool>();
            election.LeadershipChanged += _ => throw new InvalidOperationException("Subscriber failed.");
            election.LeadershipChanged += observed.Add;

            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            Assert.That(election.IsLeader, Is.True);
            Assert.That(observed, Has.Count.EqualTo(1));
            Assert.That(observed[0], Is.True);
            Assert.That(logger.ErrorCount, Is.EqualTo(1));

            await election.DisposeAsync().ConfigureAwait(false);

            Assert.That(election.IsLeader, Is.False);
            Assert.That(observed, Is.EqualTo(s_acquireThenLoss));
            Assert.That(logger.ErrorCount, Is.EqualTo(2));
            (bool found, _) = await store.TryGetAsync(LeaseKey).ConfigureAwait(false);
            Assert.That(found, Is.False);
        }

        /// <summary>
        /// Verifies lease expiry reaches healthy subscribers even when an earlier subscriber throws.
        /// </summary>
        [Test]
        public async Task ExpiryNotifiesOtherSubscribersWhenOneThrowsAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            var logger = new RecordingLogger();
            await using SharedStoreLeaseElection election = CreateElection(store, "A", time, logger: logger);
            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            var observed = new List<bool>();
            election.LeadershipChanged += _ => throw new InvalidOperationException("Subscriber failed.");
            election.LeadershipChanged += observed.Add;

            Assert.That(() => time.Advance(s_leaseDuration), Throws.Nothing);

            Assert.That(election.IsLeader, Is.False);
            Assert.That(observed, Has.Count.EqualTo(1));
            Assert.That(observed[0], Is.False);
            Assert.That(logger.ErrorCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies reading expired leadership does not invoke application callbacks before the expiry timer runs.
        /// </summary>
        [Test]
        public async Task IsLeaderDoesNotDispatchExpiryNotificationsAsync()
        {
            var clock = new FakeTimeProvider();
            var timer = new Mock<ITimer>();
            timer.Setup(value => value.Change(It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>())).Returns(true);
            timer.Setup(value => value.DisposeAsync()).Returns(default(ValueTask));
            var time = new Mock<TimeProvider>();
            time.Setup(value => value.GetUtcNow()).Returns(clock.GetUtcNow);
            time.Setup(value => value.GetTimestamp()).Returns(clock.GetTimestamp);
            time.SetupGet(value => value.TimestampFrequency).Returns(clock.TimestampFrequency);
            Action fireTimer = () => throw new InvalidOperationException("The expiry timer was not created.");
            time.Setup(value => value.CreateTimer(
                    It.IsAny<TimerCallback>(),
                    It.IsAny<object?>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<TimeSpan>()))
                .Callback<TimerCallback, object?, TimeSpan, TimeSpan>(
                    (callback, state, _, _) => fireTimer = () => callback(state))
                .Returns(timer.Object);
            using var store = new InMemorySharedKeyValueStore();
            await using SharedStoreLeaseElection election = CreateElection(store, "A", time.Object);
            var observed = new List<bool>();
            election.LeadershipChanged += observed.Add;
            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);

            clock.Advance(s_leaseDuration);

            Assert.That(election.IsLeader, Is.False);
            Assert.That(observed, Has.Count.EqualTo(1), "Reading leadership must not invoke application callbacks.");
            Assert.That(observed[0], Is.True);

            fireTimer();

            Assert.That(observed, Is.EqualTo(s_acquireThenLoss));
        }

        private static SharedStoreLeaseElection CreateElection(
            ISharedKeyValueStore store,
            string nodeId,
            TimeProvider timeProvider,
            TimeSpan? renewInterval = null,
            ILogger? logger = null)
        {
            return new SharedStoreLeaseElection(
                store,
                LeaseKey,
                nodeId,
                s_leaseDuration,
                renewInterval ?? s_renewInterval,
                timeProvider,
                logger);
        }

        private static async Task WaitWithTimeoutAsync(Task task, string message)
        {
            Task winner = await Task.WhenAny(task, Task.Delay(s_timeout)).ConfigureAwait(false);
            Assert.That(winner, Is.SameAs(task), message);
            await task.ConfigureAwait(false);
        }

        /// <summary>
        /// Minimal <see cref="ILogger"/> that signals the first error entry.
        /// </summary>
        private sealed class RecordingLogger : ILogger
        {
            private readonly TaskCompletionSource<bool> m_errorLogged =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets completion of the first observed error-level log entry.
            /// </summary>
            public Task ErrorLogged => m_errorLogged.Task;

            /// <summary>
            /// Gets the number of error-level messages observed across concurrent election callbacks.
            /// </summary>
            public int ErrorCount => Volatile.Read(ref m_errorCount);

            /// <inheritdoc/>
            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
            {
                return NullScope.Instance;
            }

            /// <inheritdoc/>
            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            /// <summary>
            /// Counts error messages and signals the test when an election failure is logged.
            /// </summary>
            /// <typeparam name="TState">The structured state supplied by the logging caller.</typeparam>
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Error)
                {
                    Interlocked.Increment(ref m_errorCount);
                    m_errorLogged.TrySetResult(true);
                }
            }

            private int m_errorCount;

            private sealed class NullScope : IDisposable
            {
                public static NullScope Instance { get; } = new();

                public void Dispose()
                {
                }
            }
        }
    }
}
