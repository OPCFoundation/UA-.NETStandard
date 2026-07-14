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

// CA2025: the concurrent-acquire tasks are always awaited (Task.WhenAll)
// before the coordinators they capture are disposed by the enclosing
// `await using`; the analyzer cannot prove the ordering across the local
// task variables. Disabled file-level for the suite.
#pragma warning disable CA2025

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Two-replica unit tests for the distributed
    /// <see cref="DistributedPushConfigurationTransactionCoordinator"/>: shared
    /// lease acquisition, cross-replica exclusion, expiry/crash recovery,
    /// renewal, and release on apply/cancel/reset. The background renew loop is
    /// suppressed and reconciliation is driven deterministically with a
    /// <see cref="FakeTimeProvider"/>.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class DistributedPushConfigurationTransactionCoordinatorTests
    {
        private static readonly TimeSpan s_leaseDuration = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan s_renewInterval = TimeSpan.FromSeconds(10);

        private ITelemetryContext m_telemetry = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void ConstructorNullArgumentsThrow()
        {
            using var store = new InMemorySharedKeyValueStore();
            var options = NewOptions("A");
            var time = new FakeTimeProvider();

            Assert.That(
                () => new DistributedPushConfigurationTransactionCoordinator(
                    null, null!, options, m_telemetry, time),
                Throws.ArgumentNullException);
            Assert.That(
                () => new DistributedPushConfigurationTransactionCoordinator(
                    null, store, null!, m_telemetry, time),
                Throws.ArgumentNullException);
            Assert.That(
                () => new DistributedPushConfigurationTransactionCoordinator(
                    null, store, options, null!, time),
                Throws.ArgumentNullException);
        }

        [Test]
        public void OptionsValidateRejectsInconsistentIntervals()
        {
            var options = new DistributedPushConfigurationOptions
            {
                ReplicaId = "A",
                LeaseDuration = TimeSpan.FromSeconds(10),
                RenewInterval = TimeSpan.FromSeconds(10)
            };

            Assert.That(() => options.Validate(), Throws.ArgumentException);
        }

        [Test]
        public async Task FirstReplicaAcquiresOwnershipAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using DistributedPushConfigurationTransactionCoordinator a = Create(store, "A", time);

            await a.AcquireTransactionOwnershipAsync(Session("s1"));

            Assert.That(a.HoldsLease, Is.True);
        }

        [Test]
        public async Task SecondReplicaRejectedWhileFirstHoldsLeaseAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using DistributedPushConfigurationTransactionCoordinator a = Create(store, "A", time);
            await using DistributedPushConfigurationTransactionCoordinator b = Create(store, "B", time);

            await a.AcquireTransactionOwnershipAsync(Session("s1"));

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await b.AcquireTransactionOwnershipAsync(Session("s2")));
            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTransactionPending));
            Assert.That(b.HoldsLease, Is.False);
        }

        [Test]
        public async Task StandbyTakesOverAfterOwnerLeaseExpiresAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using DistributedPushConfigurationTransactionCoordinator a = Create(store, "A", time);
            await using DistributedPushConfigurationTransactionCoordinator b = Create(store, "B", time);

            await a.AcquireTransactionOwnershipAsync(Session("s1"));

            // Simulate the owner crashing: it stops renewing and the lease
            // expires. The renew loop is suppressed, so no renewal happens.
            time.Advance(s_leaseDuration + TimeSpan.FromSeconds(1));

            await b.AcquireTransactionOwnershipAsync(Session("s2"));

            Assert.That(b.HoldsLease, Is.True);
        }

        [Test]
        public async Task ReacquireRenewsLeaseAndKeepsStandbyOutAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using DistributedPushConfigurationTransactionCoordinator a = Create(store, "A", time);
            await using DistributedPushConfigurationTransactionCoordinator b = Create(store, "B", time);

            await a.AcquireTransactionOwnershipAsync(Session("s1"));

            // The owner renews before the original expiry, extending it.
            time.Advance(TimeSpan.FromSeconds(20));
            await a.AcquireTransactionOwnershipAsync(Session("s1"));

            // Past the original expiry but within the renewed window: the
            // standby must still be refused.
            time.Advance(TimeSpan.FromSeconds(15));
            Assert.That(
                async () => await b.AcquireTransactionOwnershipAsync(Session("s2")),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public async Task ReconcileRenewsLeaseWhileTransactionActiveAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using DistributedPushConfigurationTransactionCoordinator a = Create(store, "A", time);
            await using DistributedPushConfigurationTransactionCoordinator b = Create(store, "B", time);

            await a.AcquireTransactionOwnershipAsync(Session("s1"));
            a.Stage(Session("s1"), NoopOperation());

            // The background loop would renew here; drive one reconcile pass
            // deterministically while the transaction is active.
            time.Advance(TimeSpan.FromSeconds(20));
            await a.ReconcileNowAsync();

            // Beyond the original expiry, but the reconcile renewed the lease.
            time.Advance(TimeSpan.FromSeconds(15));
            Assert.That(
                async () => await b.AcquireTransactionOwnershipAsync(Session("s2")),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(a.HoldsLease, Is.True);
        }

        [Test]
        public async Task ApplyChangesReleasesLeaseAndLetsStandbyTakeOverAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using DistributedPushConfigurationTransactionCoordinator a = Create(store, "A", time);
            await using DistributedPushConfigurationTransactionCoordinator b = Create(store, "B", time);

            NodeId session = Session("s1");
            await a.AcquireTransactionOwnershipAsync(session);
            a.Stage(session, NoopOperation());

            ServiceResult result = await a.ApplyChangesAsync(session);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(a.HoldsLease, Is.False);

            // The lease is released immediately (no need to wait for expiry).
            await b.AcquireTransactionOwnershipAsync(Session("s2"));
            Assert.That(b.HoldsLease, Is.True);
        }

        [Test]
        public async Task CancelChangesReleasesLeaseAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using DistributedPushConfigurationTransactionCoordinator a = Create(store, "A", time);
            await using DistributedPushConfigurationTransactionCoordinator b = Create(store, "B", time);

            NodeId session = Session("s1");
            await a.AcquireTransactionOwnershipAsync(session);
            a.Stage(session, NoopOperation());

            ServiceResult result = a.CancelChanges(session);
            // The sync cancel signals the (suppressed) loop; drive the release.
            await a.ReconcileNowAsync();

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(a.HoldsLease, Is.False);
            await b.AcquireTransactionOwnershipAsync(Session("s2"));
            Assert.That(b.HoldsLease, Is.True);
        }

        [Test]
        public async Task ResetReleasesLeaseAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using DistributedPushConfigurationTransactionCoordinator a = Create(store, "A", time);

            NodeId session = Session("s1");
            await a.AcquireTransactionOwnershipAsync(session);
            a.Stage(session, NoopOperation());

            a.Reset();
            await a.ReconcileNowAsync();

            Assert.That(a.HoldsLease, Is.False);
            Assert.That(a.IsTransactionActive, Is.False);
        }

        [Test]
        public async Task SessionCloseReleasesLeaseForOwningSessionAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using DistributedPushConfigurationTransactionCoordinator a = Create(store, "A", time);
            await using DistributedPushConfigurationTransactionCoordinator b = Create(store, "B", time);

            NodeId session = Session("s1");
            await a.AcquireTransactionOwnershipAsync(session);
            a.Stage(session, NoopOperation());

            a.CancelForSessionClose(session);
            await a.ReconcileNowAsync();

            Assert.That(a.HoldsLease, Is.False);
            await b.AcquireTransactionOwnershipAsync(Session("s2"));
            Assert.That(b.HoldsLease, Is.True);
        }

        [Test]
        public async Task ConcurrentAcquireLetsExactlyOneReplicaWinAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using DistributedPushConfigurationTransactionCoordinator a = Create(store, "A", time);
            await using DistributedPushConfigurationTransactionCoordinator b = Create(store, "B", time);

            Task<bool> ta = TryAcquireAsync(a, Session("s1"));
            Task<bool> tb = TryAcquireAsync(b, Session("s2"));
            bool[] results = await Task.WhenAll(ta, tb);

            int winners = (results[0] ? 1 : 0) + (results[1] ? 1 : 0);
            Assert.That(winners, Is.EqualTo(1), "exactly one replica may own the transaction");
        }

        [Test]
        public async Task ForeignOwnerGuardRejectsLocalStageAndValidateAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using DistributedPushConfigurationTransactionCoordinator a = Create(store, "A", time);
            await using DistributedPushConfigurationTransactionCoordinator b = Create(store, "B", time);

            await a.AcquireTransactionOwnershipAsync(Session("s1"));

            // The standby observes the foreign lease during a reconcile pass.
            await b.ReconcileNowAsync();

            Assert.That(
                () => b.ValidateSessionCanParticipate(Session("s2")),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(
                () => b.Stage(Session("s2"), NoopOperation()),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public async Task RequireLeadershipRejectsNonLeaderAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            var options = NewOptions("A");
            options.RequireLeadership = true;
            await using DistributedPushConfigurationTransactionCoordinator a =
                new(null, store, options, m_telemetry, time, new FixedLeaderElection(false), startRenewLoop: false);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await a.AcquireTransactionOwnershipAsync(Session("s1")));
            Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTransactionPending));
            Assert.That(a.HoldsLease, Is.False);
        }

        [Test]
        public async Task RequireLeadershipAllowsLeaderAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            var options = NewOptions("A");
            options.RequireLeadership = true;
            await using DistributedPushConfigurationTransactionCoordinator a =
                new(null, store, options, m_telemetry, time, new FixedLeaderElection(true), startRenewLoop: false);

            await a.AcquireTransactionOwnershipAsync(Session("s1"));

            Assert.That(a.HoldsLease, Is.True);
        }

        [Test]
        public async Task InMemoryDevPathRunsFullTransactionLifecycleAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using DistributedPushConfigurationTransactionCoordinator a = Create(store, "A", time);

            NodeId session = Session("s1");
            await a.AcquireTransactionOwnershipAsync(session);
            a.Stage(session, NoopOperation());
            Assert.That(a.IsTransactionActive, Is.True);

            ServiceResult result = await a.ApplyChangesAsync(session);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(a.IsTransactionActive, Is.False);
            Assert.That(a.HoldsLease, Is.False);
        }

        [Test]
        public async Task AcquireAfterDisposeThrowsObjectDisposedAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            DistributedPushConfigurationTransactionCoordinator a = Create(store, "A", time);
            await a.DisposeAsync();

            Assert.That(
                async () => await a.AcquireTransactionOwnershipAsync(Session("s1")),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task DelegatesSnapshotToInnerCoordinatorAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using DistributedPushConfigurationTransactionCoordinator a = Create(store, "A", time);

            NodeId session = Session("s1");
            await a.AcquireTransactionOwnershipAsync(session);
            a.Stage(session, NoopOperation());

            PushConfigurationTransactionSnapshot snapshot = a.GetSnapshot();
            Assert.That(snapshot.IsActive, Is.True);
            Assert.That(snapshot.OwnerSessionId, Is.EqualTo(session));
        }

        [Test]
        public async Task NonLeaderRejectionNeverAcquiresLeaseAcrossReconcilesAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using DistributedPushConfigurationTransactionCoordinator a =
                Create(store, "A", time, leaderElection: new FixedLeaderElection(false));

            // A non-leader is refused and must not reserve ownership.
            Assert.ThrowsAsync<ServiceResultException>(
                async () => await a.AcquireTransactionOwnershipAsync(Session("s1")));
            Assert.That(a.HoldsLease, Is.False);

            // Drive reconcile passes inside and beyond the reservation window and
            // advance time: a leaked reservation would let the loop acquire the
            // lease here. The rejected non-leader must never acquire or hold it.
            await a.ReconcileNowAsync();
            Assert.That(a.HoldsLease, Is.False);

            time.Advance(s_renewInterval);
            await a.ReconcileNowAsync();
            Assert.That(a.HoldsLease, Is.False);

            time.Advance(s_leaseDuration);
            await a.ReconcileNowAsync();
            Assert.That(a.HoldsLease, Is.False, "a rejected non-leader must never acquire the lease");

            (bool found, ByteString lease) = await store.TryGetAsync(LeaseKey());
            Assert.That(found && LeaseOwner(lease) == "A", Is.False,
                "the shared store must never record the rejected non-leader as the lease owner");
        }

        [Test]
        public async Task ForeignOwnerRejectionNeverSelfAcquiresLeaseAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            // A reservation timeout longer than the lease makes any leaked
            // reservation outlive the owner's lease - exactly the window a
            // self-promotion bug would exploit once the owner "crashes".
            TimeSpan longReservation = s_leaseDuration + s_leaseDuration;
            await using DistributedPushConfigurationTransactionCoordinator a =
                Create(store, "A", time, reservationTimeout: longReservation);
            await using DistributedPushConfigurationTransactionCoordinator b =
                Create(store, "B", time, reservationTimeout: longReservation);

            await a.AcquireTransactionOwnershipAsync(Session("s1"));

            // B is refused because A holds a live lease; it must not reserve.
            Assert.ThrowsAsync<ServiceResultException>(
                async () => await b.AcquireTransactionOwnershipAsync(Session("s2")));
            Assert.That(b.HoldsLease, Is.False);

            // While A's lease is live, B's reconcile must back off.
            await b.ReconcileNowAsync();
            Assert.That(b.HoldsLease, Is.False, "B must back off while A's lease is live");

            // A "crashes": its lease lapses while B's would-be leaked reservation
            // is still within its (longer) window. B must still never self-acquire.
            time.Advance(s_leaseDuration + TimeSpan.FromSeconds(1));
            for (int i = 0; i < 3; i++)
            {
                await b.ReconcileNowAsync();
                Assert.That(b.HoldsLease, Is.False,
                    "a rejected standby must not self-acquire a lapsed lease from a leaked reservation");
                time.Advance(s_renewInterval);
            }

            (bool found, ByteString lease) = await store.TryGetAsync(LeaseKey());
            Assert.That(found && LeaseOwner(lease) == "B", Is.False,
                "the shared store must never record the rejected standby as the lease owner");
        }

        [Test]
        public async Task CancelledAcquisitionLeavesNoReservationAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            await using DistributedPushConfigurationTransactionCoordinator a =
                Create(store, "A", time, reservationTimeout: s_leaseDuration + s_leaseDuration);

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await a.AcquireTransactionOwnershipAsync(Session("s1"), cts.Token));
            Assert.That(a.HoldsLease, Is.False);

            // A cancelled acquisition must not leave a reservation that the
            // reconcile loop could later use to acquire the lease.
            await a.ReconcileNowAsync();
            Assert.That(a.HoldsLease, Is.False);
            time.Advance(s_renewInterval);
            await a.ReconcileNowAsync();
            Assert.That(a.HoldsLease, Is.False, "a cancelled acquisition must never acquire the lease");

            (bool found, _) = await store.TryGetAsync(LeaseKey());
            Assert.That(found, Is.False, "a cancelled acquisition must not create a lease");
        }

        [Test]
        public async Task RepeatedNonLeaderRejectionsDoNotStarveLeaderAsync()
        {
            var time = new FakeTimeProvider();
            using var store = new InMemorySharedKeyValueStore();
            TimeSpan longReservation = s_leaseDuration + s_leaseDuration;
            await using DistributedPushConfigurationTransactionCoordinator leader = Create(
                store, "A", time, leaderElection: new FixedLeaderElection(true),
                reservationTimeout: longReservation);
            await using DistributedPushConfigurationTransactionCoordinator standby = Create(
                store, "B", time, leaderElection: new FixedLeaderElection(false),
                reservationTimeout: longReservation);

            // The leader owns an active transaction and renews it as time passes.
            NodeId session = Session("s1");
            await leader.AcquireTransactionOwnershipAsync(session);
            leader.Stage(session, NoopOperation());
            Assert.That(leader.HoldsLease, Is.True);

            // Hammer the standby with rejected acquisitions while both reconcile
            // and time advances. The leader must keep the lease throughout and
            // the rejected standby must never acquire it (no starvation).
            for (int i = 0; i < 8; i++)
            {
                Assert.ThrowsAsync<ServiceResultException>(
                    async () => await standby.AcquireTransactionOwnershipAsync(Session("s-standby")));
                Assert.That(standby.HoldsLease, Is.False);

                time.Advance(s_renewInterval);
                await leader.ReconcileNowAsync();
                await standby.ReconcileNowAsync();

                Assert.That(leader.HoldsLease, Is.True, "the leader must retain the lease");
                Assert.That(standby.HoldsLease, Is.False,
                    "repeated rejected traffic must not let the standby acquire the lease");

                (bool found, ByteString lease) = await store.TryGetAsync(LeaseKey());
                Assert.That(found, Is.True);
                Assert.That(LeaseOwner(lease), Is.EqualTo("A"),
                    "the lease must remain owned by the leader across rejected standby traffic");
            }

            // The leader can still complete its transaction: it was never starved.
            ServiceResult result = await leader.ApplyChangesAsync(session);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        private static string LeaseKey()
        {
            return new DistributedPushConfigurationOptions { ReplicaId = "x" }.TransactionLeaseKey;
        }

        private static string? LeaseOwner(ByteString raw)
        {
            if (raw.IsNull)
            {
                return null;
            }
            byte[] bytes = raw.ToArray();
            if (bytes.Length < 4)
            {
                return null;
            }
            int ownerLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, 4));
            if (ownerLength < 0 || bytes.Length < 4 + ownerLength + 8)
            {
                return null;
            }
            return System.Text.Encoding.UTF8.GetString(bytes, 4, ownerLength);
        }

        private static async Task<bool> TryAcquireAsync(
            DistributedPushConfigurationTransactionCoordinator coordinator,
            NodeId sessionId)
        {
            try
            {
                await coordinator.AcquireTransactionOwnershipAsync(sessionId);
                return true;
            }
            catch (ServiceResultException)
            {
                return false;
            }
        }

        private DistributedPushConfigurationTransactionCoordinator Create(
            InMemorySharedKeyValueStore store,
            string replicaId,
            FakeTimeProvider time,
            ILeaderElection? leaderElection = null,
            TimeSpan? reservationTimeout = null)
        {
            var options = NewOptions(replicaId);
            options.RequireLeadership = leaderElection != null;
            if (reservationTimeout.HasValue)
            {
                options.ReservationTimeout = reservationTimeout.Value;
            }
            return new DistributedPushConfigurationTransactionCoordinator(
                null, store, options, m_telemetry, time, leaderElection, startRenewLoop: false);
        }

        private static DistributedPushConfigurationOptions NewOptions(string replicaId)
        {
            return new DistributedPushConfigurationOptions
            {
                ReplicaId = replicaId,
                LeaseDuration = s_leaseDuration,
                RenewInterval = s_renewInterval
            };
        }

        private static NodeId Session(string id)
        {
            return new NodeId(id, 1);
        }

        private static PushConfigurationOperation NoopOperation()
        {
            return new PushConfigurationOperation
            {
                CommitAsync = _ => Task.CompletedTask
            };
        }

        private sealed class FixedLeaderElection : ILeaderElection
        {
            public FixedLeaderElection(bool isLeader)
            {
                IsLeader = isLeader;
            }

            public bool IsLeader { get; }

            public event Action<bool>? LeadershipChanged;

            public ValueTask<bool> TryAcquireOrRenewAsync(CancellationToken ct = default)
            {
                LeadershipChanged?.Invoke(IsLeader);
                return new ValueTask<bool>(IsLeader);
            }

            public void Start()
            {
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }
        }
    }
}
