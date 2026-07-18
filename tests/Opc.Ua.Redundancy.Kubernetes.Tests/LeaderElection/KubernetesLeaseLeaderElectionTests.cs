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

// CA2000: system-under-test disposables are created per test and released at teardown;
//   there is no cross-test resource leak. Suppressed file-level for the suite.
#pragma warning disable CA2000 // Dispose objects before losing scope

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Redundancy.Kubernetes.Tests
{
    /// <summary>
    /// Unit tests for Kubernetes Lease leader election.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class KubernetesLeaseLeaderElectionTests
    {
        [Test]
        public async Task MissingLeaseCreatesLeaseAndBecomesLeaderAsync()
        {
            Mock<IKubernetesApiClient> api = NewApi();
            var time = new FakeTimeProvider(ParseUtc("2026-01-01T00:00:00Z"));
            KubernetesLease? created = null;
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>()))
                .ReturnsAsync((KubernetesLease?)null);
            api.Setup(x => x.CreateLeaseAsync("ns", It.IsAny<KubernetesLease>(), It.IsAny<CancellationToken>()))
                .Callback<string, KubernetesLease, CancellationToken>((_, lease, _) => created = lease)
                .ReturnsAsync((string _, KubernetesLease lease, CancellationToken _) => lease);

            await using var election = new KubernetesLeaseLeaderElection(api.Object, NewOptions(), time);
            bool acquired = await election.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(acquired, Is.True);
            Assert.That(election.IsLeader, Is.True);
            Assert.That(created?.Spec.HolderIdentity, Is.EqualTo("pod-a"));
            Assert.That(created?.Spec.LeaseDurationSeconds, Is.EqualTo(30));
        }

        [Test]
        public async Task SameHolderRenewsLeaseAsync()
        {
            Mock<IKubernetesApiClient> api = NewApi();
            KubernetesLease lease = HeldLease("pod-a", ParseUtc("2026-01-01T00:00:00Z"));
            var time = new FakeTimeProvider(ParseUtc("2026-01-01T00:00:10Z"));
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>())).ReturnsAsync(lease);
            api.Setup(x => x.ReplaceLeaseAsync("ns", "opcua", lease, It.IsAny<CancellationToken>()))
                .ReturnsAsync(lease);

            await using var election = new KubernetesLeaseLeaderElection(api.Object, NewOptions(), time);
            bool acquired = await election.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(acquired, Is.True);
            Assert.That(lease.Spec.HolderIdentity, Is.EqualTo("pod-a"));
            Assert.That(lease.Spec.LeaseTransitions, Is.Zero);
        }

        [Test]
        public async Task LiveForeignHolderKeepsReplicaFollowerAsync()
        {
            Mock<IKubernetesApiClient> api = NewApi();
            KubernetesLease lease = HeldLease("pod-b", ParseUtc("2026-01-01T00:00:00Z"));
            var time = new FakeTimeProvider(ParseUtc("2026-01-01T00:00:10Z"));
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>())).ReturnsAsync(lease);

            await using var election = new KubernetesLeaseLeaderElection(api.Object, NewOptions(), time);
            bool acquired = await election.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(acquired, Is.False);
            Assert.That(election.IsLeader, Is.False);
            api.Verify(x => x.ReplaceLeaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<KubernetesLease>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ForeignHolderCanBeTakenOverAfterUnchangedResourceVersionDurationAsync()
        {
            Mock<IKubernetesApiClient> api = NewApi();
            KubernetesLease lease = HeldLease("pod-b", ParseUtc("2100-01-01T00:00:00Z"));
            var time = new FakeTimeProvider(ParseUtc("2026-01-01T00:00:00Z"));
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>())).ReturnsAsync(lease);
            api.Setup(x => x.ReplaceLeaseAsync("ns", "opcua", lease, It.IsAny<CancellationToken>()))
                .ReturnsAsync(lease);

            await using var election = new KubernetesLeaseLeaderElection(api.Object, NewOptions(), time);
            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.False);
            time.Advance(TimeSpan.FromSeconds(29));
            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.False);
            time.Advance(TimeSpan.FromSeconds(1));
            bool acquired = await election.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(acquired, Is.True);
            Assert.That(lease.Spec.HolderIdentity, Is.EqualTo("pod-a"));
            Assert.That(lease.Spec.LeaseTransitions, Is.EqualTo(1));
        }

        [Test]
        public async Task ResourceVersionChangeRestartsForeignLeaseObservationAsync()
        {
            Mock<IKubernetesApiClient> api = NewApi();
            KubernetesLease lease = HeldLease("pod-b", ParseUtc("2000-01-01T00:00:00Z"));
            var time = new FakeTimeProvider(ParseUtc("2026-01-01T00:00:00Z"));
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>())).ReturnsAsync(lease);
            api.Setup(x => x.ReplaceLeaseAsync("ns", "opcua", lease, It.IsAny<CancellationToken>()))
                .ReturnsAsync(lease);

            await using var election = new KubernetesLeaseLeaderElection(api.Object, NewOptions(), time);
            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.False);
            time.Advance(TimeSpan.FromSeconds(29));
            lease.Metadata.ResourceVersion = "2";
            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.False);
            time.Advance(TimeSpan.FromSeconds(29));
            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.False);
            time.Advance(TimeSpan.FromSeconds(1));

            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            Assert.That(lease.Spec.HolderIdentity, Is.EqualTo("pod-a"));
        }

        [Test]
        public async Task ConflictLosesLeadershipAsync()
        {
            Mock<IKubernetesApiClient> api = NewApi();
            KubernetesLease lease = HeldLease("pod-a", ParseUtc("2026-01-01T00:00:00Z"));
            var time = new FakeTimeProvider(ParseUtc("2026-01-01T00:00:10Z"));
            api.SetupSequence(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>()))
                .ReturnsAsync((KubernetesLease?)null)
                .ReturnsAsync(lease);
            api.Setup(x => x.CreateLeaseAsync("ns", It.IsAny<KubernetesLease>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, KubernetesLease newLease, CancellationToken _) => newLease);
            api.Setup(x => x.ReplaceLeaseAsync("ns", "opcua", lease, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("conflict", null, HttpStatusCode.Conflict));

            await using var election = new KubernetesLeaseLeaderElection(api.Object, NewOptions(), time);
            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.False);
            Assert.That(election.IsLeader, Is.False);
        }

        [Test]
        public async Task DisposeSuppressesQueuedStaleTrueNotificationAsync()
        {
            Mock<IKubernetesApiClient> api = NewApi();
            api.SetupSequence(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>()))
                .ReturnsAsync((KubernetesLease?)null)
                .ReturnsAsync((KubernetesLease?)null);
            api.Setup(x => x.CreateLeaseAsync("ns", It.IsAny<KubernetesLease>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, KubernetesLease lease, CancellationToken _) => lease);
            var election = new KubernetesLeaseLeaderElection(
                api.Object,
                NewOptions(),
                new FakeTimeProvider(ParseUtc("2026-01-01T00:00:00Z")));
            var notifications = new List<bool>();
            var notificationsLock = new Lock();
            election.LeadershipChanged += value =>
            {
                lock (notificationsLock)
                {
                    notifications.Add(value);
                }
            };
            FieldInfo? notificationLockField = typeof(KubernetesLeaseLeaderElection).GetField(
                "m_notificationLock",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(notificationLockField, Is.Not.Null);
            var notificationLock = (Lock)notificationLockField!.GetValue(election)!;

            Task<bool> acquireTask;
            Task disposeTask;
            lock (notificationLock)
            {
                acquireTask = Task.Run(
                    async () => await election.TryAcquireOrRenewAsync().ConfigureAwait(false));
                Assert.That(
                    SpinWait.SpinUntil(() => election.IsLeader, TimeSpan.FromSeconds(10)),
                    Is.True);
                disposeTask = Task.Run(
                    async () => await election.DisposeAsync().ConfigureAwait(false));
                Assert.That(
                    SpinWait.SpinUntil(() => !election.IsLeader, TimeSpan.FromSeconds(10)),
                    Is.True);
            }

            Assert.That(await acquireTask.ConfigureAwait(false), Is.True);
            await disposeTask.ConfigureAwait(false);
            lock (notificationsLock)
            {
                Assert.That(notifications, Has.Count.EqualTo(1));
                Assert.That(notifications[0], Is.False);
            }
        }

        [Test]
        public async Task DisposeReleasesOwnedLeaseAsync()
        {
            Mock<IKubernetesApiClient> api = NewApi();
            KubernetesLease lease = HeldLease("pod-a", ParseUtc("2026-01-01T00:00:00Z"));
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>())).ReturnsAsync(lease);
            api.Setup(x => x.ReplaceLeaseAsync("ns", "opcua", lease, It.IsAny<CancellationToken>()))
                .ReturnsAsync(lease);

            var election = new KubernetesLeaseLeaderElection(api.Object, NewOptions(), new FakeTimeProvider());
            await election.DisposeAsync().ConfigureAwait(false);

            Assert.That(lease.Spec.HolderIdentity, Is.Null);
        }

        [Test]
        public async Task NotInClusterCannotAcquireLeadershipAsync()
        {
            var api = new Mock<IKubernetesApiClient>(MockBehavior.Strict);
            api.SetupGet(x => x.IsInCluster).Returns(false);

            await using var election = new KubernetesLeaseLeaderElection(api.Object, NewOptions(), new FakeTimeProvider());
            bool acquired = await election.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(acquired, Is.False);
            Assert.That(election.IsLeader, Is.False);
        }

        [Test]
        public async Task EmptyLeaseHolderCanBeAcquiredAsync()
        {
            Mock<IKubernetesApiClient> api = NewApi();
            KubernetesLease lease = HeldLease(string.Empty, ParseUtc("2026-01-01T00:00:00Z"));
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>())).ReturnsAsync(lease);
            api.Setup(x => x.ReplaceLeaseAsync("ns", "opcua", lease, It.IsAny<CancellationToken>()))
                .ReturnsAsync(lease);

            await using var election = new KubernetesLeaseLeaderElection(
                api.Object,
                NewOptions(),
                new FakeTimeProvider(ParseUtc("2026-01-01T00:00:05Z")));
            bool acquired = await election.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(acquired, Is.True);
            Assert.That(lease.Spec.HolderIdentity, Is.EqualTo("pod-a"));
        }

        [Test]
        public async Task MissingRenewTimeStillRequiresLocalObservationWindowAsync()
        {
            Mock<IKubernetesApiClient> api = NewApi();
            KubernetesLease lease = HeldLease("pod-b", ParseUtc("2026-01-01T00:00:00Z"));
            lease.Spec.RenewTime = null;
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>())).ReturnsAsync(lease);
            api.Setup(x => x.ReplaceLeaseAsync("ns", "opcua", lease, It.IsAny<CancellationToken>()))
                .ReturnsAsync(lease);
            var time = new FakeTimeProvider(ParseUtc("2026-01-01T00:00:05Z"));

            await using var election = new KubernetesLeaseLeaderElection(
                api.Object,
                NewOptions(),
                time);
            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.False);
            time.Advance(TimeSpan.FromSeconds(30));
            bool acquired = await election.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(acquired, Is.True);
            Assert.That(lease.Spec.HolderIdentity, Is.EqualTo("pod-a"));
        }

        [Test]
        public async Task StartIsIdempotentAndDisposeStopsRenewLoopAsync()
        {
            var api = new Mock<IKubernetesApiClient>(MockBehavior.Strict);
            api.SetupGet(x => x.IsInCluster).Returns(false);
            KubernetesLeaderElectionOptions options = NewOptions();
            options.RenewInterval = TimeSpan.FromMilliseconds(1);

            var election = new KubernetesLeaseLeaderElection(api.Object, options, new FakeTimeProvider());
            election.Start();
            election.Start();

            await election.DisposeAsync().ConfigureAwait(false);

            Assert.That(election.IsLeader, Is.False);
        }

        [Test]
        public Task DisposeIgnoresReleaseFailureAsync()
        {
            Mock<IKubernetesApiClient> api = NewApi();
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("transient"));

            var election = new KubernetesLeaseLeaderElection(api.Object, NewOptions(), new FakeTimeProvider());

            Assert.DoesNotThrowAsync(async () => await election.DisposeAsync().AsTask().ConfigureAwait(false));
            return Task.CompletedTask;
        }

        [Test]
        public void StaticReadinessComparesServiceLevel()
        {
            Assert.That(KubernetesLeaseLeaderElection.IsReadyServiceLevel(200, 100), Is.True);
            Assert.That(KubernetesLeaseLeaderElection.IsReadyServiceLevel(99, 100), Is.False);
        }

        [Test]
        public void PublicConstructorRejectsNullOptions()
        {
            Assert.Throws<ArgumentNullException>(() => new KubernetesLeaseLeaderElection(null!));
        }

        [Test]
        public async Task PublicConstructorBuildsElectionOutsideClusterAsync()
        {
            await using var election = new KubernetesLeaseLeaderElection(NewOptions());

            bool acquired = await election.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(acquired, Is.False);
            Assert.That(election.IsLeader, Is.False);
        }

        [Test]
        public async Task DisposeIsIdempotentAsync()
        {
            var api = new Mock<IKubernetesApiClient>(MockBehavior.Strict);
            api.SetupGet(x => x.IsInCluster).Returns(false);

            var election = new KubernetesLeaseLeaderElection(api.Object, NewOptions(), new FakeTimeProvider());
            await election.DisposeAsync().ConfigureAwait(false);
            await election.DisposeAsync().ConfigureAwait(false);

            Assert.That(election.IsLeader, Is.False);
        }

        [Test]
        public async Task RenewLoopLogsAndContinuesOnErrorAsync()
        {
            var api = new Mock<IKubernetesApiClient>();
            api.SetupGet(x => x.IsInCluster).Returns(true);
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>()))
                .ReturnsAsync((KubernetesLease?)null);
            api.Setup(x => x.CreateLeaseAsync("ns", It.IsAny<KubernetesLease>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, KubernetesLease lease, CancellationToken _) => lease);
            var logger = new CountingLogger();
            KubernetesLeaderElectionOptions options = NewOptions();
            options.RenewInterval = TimeSpan.FromMilliseconds(5);

            var election = new KubernetesLeaseLeaderElection(api.Object, options, new FakeTimeProvider(), logger);
            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            Assert.That(election.IsLeader, Is.True);
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>()))
                .Callback(() => signal.TrySetResult())
                .ThrowsAsync(new InvalidOperationException("boom"));
            election.Start();
            await signal.Task.ConfigureAwait(false);
            await election.DisposeAsync().ConfigureAwait(false);

            Assert.That(logger.ErrorCount, Is.GreaterThan(0));
            Assert.That(election.IsLeader, Is.False);
        }

        [Test]
        public async Task RenewalFailureWithoutHttpStatusClearsLeadershipAsync()
        {
            Mock<IKubernetesApiClient> api = NewApi();
            api.SetupSequence(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>()))
                .ReturnsAsync((KubernetesLease?)null)
                .ThrowsAsync(new HttpRequestException("connection lost"));
            api.Setup(x => x.CreateLeaseAsync("ns", It.IsAny<KubernetesLease>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, KubernetesLease createdLease, CancellationToken _) => createdLease);

            await using var election = new KubernetesLeaseLeaderElection(
                api.Object,
                NewOptions(),
                new FakeTimeProvider(ParseUtc("2026-01-01T00:00:10Z")));

            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            Assert.That(election.IsLeader, Is.True);

            Assert.That(
                async () => await election.TryAcquireOrRenewAsync().ConfigureAwait(false),
                Throws.TypeOf<HttpRequestException>().With.Message.EqualTo("connection lost"));
            Assert.That(election.IsLeader, Is.False);
        }

        [Test]
        public async Task HungApiAttemptTimesOutAtRenewIntervalAsync()
        {
            Mock<IKubernetesApiClient> api = NewApi();
            var time = new FakeTimeProvider(ParseUtc("2026-01-01T00:00:00Z"));
            var getStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var hungGet = new TaskCompletionSource<KubernetesLease?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int getCalls = 0;
            CancellationToken requestToken = default;
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>()))
                .Returns((string _, string _, CancellationToken token) =>
                {
                    int call = Interlocked.Increment(ref getCalls);
                    if (call == 1)
                    {
                        requestToken = token;
                        getStarted.TrySetResult();
                        return new ValueTask<KubernetesLease?>(hungGet.Task);
                    }
                    return new ValueTask<KubernetesLease?>((KubernetesLease?)null);
                });
            KubernetesLeaderElectionOptions options = NewOptions();
            options.RenewInterval = TimeSpan.FromSeconds(5);

            await using var election = new KubernetesLeaseLeaderElection(api.Object, options, time);
            Task<bool> attempt = election.TryAcquireOrRenewAsync().AsTask();
            await getStarted.Task.ConfigureAwait(false);
            time.Advance(TimeSpan.FromSeconds(5));

            Assert.That(
                () => attempt,
                Throws.TypeOf<TimeoutException>());
            Assert.That(election.IsLeader, Is.False);
            Assert.That(requestToken.IsCancellationRequested, Is.True);
            hungGet.TrySetResult(null);
        }

        [Test]
        public async Task WatchdogFencesHungRenewalAndRejectsStaleCompletionAsync()
        {
            Mock<IKubernetesApiClient> api = NewApi();
            var time = new FakeTimeProvider(ParseUtc("2026-01-01T00:00:00Z"));
            var renewStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var hungRenew = new TaskCompletionSource<KubernetesLease?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int getCalls = 0;
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>()))
                .Returns((string _, string _, CancellationToken _) =>
                {
                    int call = Interlocked.Increment(ref getCalls);
                    if (call == 1 || call > 2)
                    {
                        return new ValueTask<KubernetesLease?>((KubernetesLease?)null);
                    }
                    renewStarted.TrySetResult();
                    return new ValueTask<KubernetesLease?>(hungRenew.Task);
                });
            api.Setup(x => x.CreateLeaseAsync("ns", It.IsAny<KubernetesLease>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, KubernetesLease lease, CancellationToken _) => lease);
            KubernetesLeaderElectionOptions options = NewOptions();
            options.RenewInterval = TimeSpan.FromMinutes(1);

            await using var election = new KubernetesLeaseLeaderElection(api.Object, options, time);
            Assert.That(await election.TryAcquireOrRenewAsync().ConfigureAwait(false), Is.True);
            Task<bool> staleAttempt = election.TryAcquireOrRenewAsync().AsTask();
            await renewStarted.Task.ConfigureAwait(false);
            time.Advance(TimeSpan.FromSeconds(29));
            Assert.That(election.IsLeader, Is.True);

            time.Advance(TimeSpan.FromSeconds(1));
            Assert.That(election.IsLeader, Is.False);
            hungRenew.TrySetResult(HeldLease("pod-a", ParseUtc("2026-01-01T00:00:00Z")));

            Assert.That(await staleAttempt.ConfigureAwait(false), Is.False);
            Assert.That(election.IsLeader, Is.False);
            api.Verify(
                x => x.ReplaceLeaseAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<KubernetesLease>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task RenewLoopStopsOnCanceledExceptionAsync()
        {
            var api = new Mock<IKubernetesApiClient>();
            api.SetupGet(x => x.IsInCluster).Returns(true);
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>()))
                .Callback(() => signal.TrySetResult())
                .ThrowsAsync(new OperationCanceledException());

            var election = new KubernetesLeaseLeaderElection(api.Object, NewOptions(), new FakeTimeProvider());
            election.Start();
            await signal.Task.ConfigureAwait(false);
            await election.DisposeAsync().ConfigureAwait(false);

            Assert.That(election.IsLeader, Is.False);
        }

        private static DateTimeOffset ParseUtc(string value)
        {
            return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
        }

        private static KubernetesLeaderElectionOptions NewOptions()
        {
            var options = new KubernetesLeaderElectionOptions
            {
                LeaseName = "opcua",
                LeaseDuration = TimeSpan.FromSeconds(30),
                RenewInterval = TimeSpan.FromSeconds(10)
            };
            options.Kubernetes.Namespace = "ns";
            options.Kubernetes.NodeId = "pod-a";
            return options;
        }

        private static KubernetesLease HeldLease(string holder, DateTimeOffset renewTime)
        {
            return new KubernetesLease
            {
                Metadata = new KubernetesObjectMetadata
                {
                    Name = "opcua",
                    Namespace = "ns",
                    ResourceVersion = "1"
                },
                Spec = new KubernetesLeaseSpec
                {
                    HolderIdentity = holder,
                    LeaseDurationSeconds = 30,
                    AcquireTime = renewTime.ToString("O", CultureInfo.InvariantCulture),
                    RenewTime = renewTime.ToString("O", CultureInfo.InvariantCulture),
                    LeaseTransitions = 0
                }
            };
        }

        private static Mock<IKubernetesApiClient> NewApi()
        {
            var api = new Mock<IKubernetesApiClient>(MockBehavior.Strict);
            api.SetupGet(x => x.IsInCluster).Returns(true);
            return api;
        }

        private sealed class CountingLogger : ILogger
        {
            public int ErrorCount => m_errorCount;

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
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
                    Interlocked.Increment(ref m_errorCount);
                }
            }

            private int m_errorCount;
        }
    }
}
