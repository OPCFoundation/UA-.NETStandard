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

            public Task ErrorLogged => m_errorLogged.Task;

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
            {
                return NullScope.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Error)
                {
                    m_errorLogged.TrySetResult(true);
                }
            }

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
