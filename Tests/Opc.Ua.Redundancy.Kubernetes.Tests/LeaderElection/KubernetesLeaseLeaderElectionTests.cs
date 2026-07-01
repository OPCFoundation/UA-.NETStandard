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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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
            var api = NewApi();
            var time = new FakeTimeProvider(ParseUtc("2026-01-01T00:00:00Z"));
            KubernetesLease? created = null;
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>()))
                .ReturnsAsync((KubernetesLease?)null);
            api.Setup(x => x.CreateLeaseAsync("ns", It.IsAny<KubernetesLease>(), It.IsAny<CancellationToken>()))
                .Callback<string, KubernetesLease, CancellationToken>((_, lease, _) => created = lease)
                .ReturnsAsync((string _, KubernetesLease lease, CancellationToken _) => lease);

            await using var election = new KubernetesLeaseLeaderElection(api.Object, NewOptions(), time);
            bool acquired = await election.TryAcquireOrRenewAsync();

            Assert.That(acquired, Is.True);
            Assert.That(election.IsLeader, Is.True);
            Assert.That(created?.Spec.HolderIdentity, Is.EqualTo("pod-a"));
            Assert.That(created?.Spec.LeaseDurationSeconds, Is.EqualTo(30));
        }

        [Test]
        public async Task SameHolderRenewsLeaseAsync()
        {
            var api = NewApi();
            var lease = HeldLease("pod-a", ParseUtc("2026-01-01T00:00:00Z"));
            var time = new FakeTimeProvider(ParseUtc("2026-01-01T00:00:10Z"));
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>())).ReturnsAsync(lease);
            api.Setup(x => x.ReplaceLeaseAsync("ns", "opcua", lease, It.IsAny<CancellationToken>()))
                .ReturnsAsync(lease);

            await using var election = new KubernetesLeaseLeaderElection(api.Object, NewOptions(), time);
            bool acquired = await election.TryAcquireOrRenewAsync();

            Assert.That(acquired, Is.True);
            Assert.That(lease.Spec.HolderIdentity, Is.EqualTo("pod-a"));
            Assert.That(lease.Spec.LeaseTransitions, Is.Zero);
        }

        [Test]
        public async Task LiveForeignHolderKeepsReplicaFollowerAsync()
        {
            var api = NewApi();
            var lease = HeldLease("pod-b", ParseUtc("2026-01-01T00:00:00Z"));
            var time = new FakeTimeProvider(ParseUtc("2026-01-01T00:00:10Z"));
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>())).ReturnsAsync(lease);

            await using var election = new KubernetesLeaseLeaderElection(api.Object, NewOptions(), time);
            bool acquired = await election.TryAcquireOrRenewAsync();

            Assert.That(acquired, Is.False);
            Assert.That(election.IsLeader, Is.False);
            api.Verify(x => x.ReplaceLeaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<KubernetesLease>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ExpiredForeignHolderCanBeTakenOverAsync()
        {
            var api = NewApi();
            var lease = HeldLease("pod-b", ParseUtc("2026-01-01T00:00:00Z"));
            var time = new FakeTimeProvider(ParseUtc("2026-01-01T00:00:31Z"));
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>())).ReturnsAsync(lease);
            api.Setup(x => x.ReplaceLeaseAsync("ns", "opcua", lease, It.IsAny<CancellationToken>()))
                .ReturnsAsync(lease);

            await using var election = new KubernetesLeaseLeaderElection(api.Object, NewOptions(), time);
            bool acquired = await election.TryAcquireOrRenewAsync();

            Assert.That(acquired, Is.True);
            Assert.That(lease.Spec.HolderIdentity, Is.EqualTo("pod-a"));
            Assert.That(lease.Spec.LeaseTransitions, Is.EqualTo(1));
        }

        [Test]
        public async Task ConflictLosesLeadershipAsync()
        {
            var api = NewApi();
            var lease = HeldLease("pod-a", ParseUtc("2026-01-01T00:00:00Z"));
            var time = new FakeTimeProvider(ParseUtc("2026-01-01T00:00:10Z"));
            api.SetupSequence(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>()))
                .ReturnsAsync((KubernetesLease?)null)
                .ReturnsAsync(lease);
            api.Setup(x => x.CreateLeaseAsync("ns", It.IsAny<KubernetesLease>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, KubernetesLease newLease, CancellationToken _) => newLease);
            api.Setup(x => x.ReplaceLeaseAsync("ns", "opcua", lease, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("conflict", null, HttpStatusCode.Conflict));

            await using var election = new KubernetesLeaseLeaderElection(api.Object, NewOptions(), time);
            Assert.That(await election.TryAcquireOrRenewAsync(), Is.True);
            Assert.That(await election.TryAcquireOrRenewAsync(), Is.False);
            Assert.That(election.IsLeader, Is.False);
        }

        [Test]
        public async Task DisposeReleasesOwnedLeaseAsync()
        {
            var api = NewApi();
            var lease = HeldLease("pod-a", ParseUtc("2026-01-01T00:00:00Z"));
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>())).ReturnsAsync(lease);
            api.Setup(x => x.ReplaceLeaseAsync("ns", "opcua", lease, It.IsAny<CancellationToken>()))
                .ReturnsAsync(lease);

            var election = new KubernetesLeaseLeaderElection(api.Object, NewOptions(), new FakeTimeProvider());
            await election.DisposeAsync();

            Assert.That(lease.Spec.HolderIdentity, Is.Null);
        }

        [Test]
        public async Task NotInClusterCannotAcquireLeadershipAsync()
        {
            var api = new Mock<IKubernetesApiClient>(MockBehavior.Strict);
            api.SetupGet(x => x.IsInCluster).Returns(false);

            await using var election = new KubernetesLeaseLeaderElection(api.Object, NewOptions(), new FakeTimeProvider());
            bool acquired = await election.TryAcquireOrRenewAsync();

            Assert.That(acquired, Is.False);
            Assert.That(election.IsLeader, Is.False);
        }

        [Test]
        public async Task EmptyLeaseHolderCanBeAcquiredAsync()
        {
            var api = NewApi();
            var lease = HeldLease(string.Empty, ParseUtc("2026-01-01T00:00:00Z"));
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>())).ReturnsAsync(lease);
            api.Setup(x => x.ReplaceLeaseAsync("ns", "opcua", lease, It.IsAny<CancellationToken>()))
                .ReturnsAsync(lease);

            await using var election = new KubernetesLeaseLeaderElection(
                api.Object,
                NewOptions(),
                new FakeTimeProvider(ParseUtc("2026-01-01T00:00:05Z")));
            bool acquired = await election.TryAcquireOrRenewAsync();

            Assert.That(acquired, Is.True);
            Assert.That(lease.Spec.HolderIdentity, Is.EqualTo("pod-a"));
        }

        [Test]
        public async Task MissingRenewTimeUsesExpiredFallbackAsync()
        {
            var api = NewApi();
            var lease = HeldLease("pod-b", ParseUtc("2026-01-01T00:00:00Z"));
            lease.Spec.RenewTime = null;
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>())).ReturnsAsync(lease);
            api.Setup(x => x.ReplaceLeaseAsync("ns", "opcua", lease, It.IsAny<CancellationToken>()))
                .ReturnsAsync(lease);

            await using var election = new KubernetesLeaseLeaderElection(
                api.Object,
                NewOptions(),
                new FakeTimeProvider(ParseUtc("2026-01-01T00:00:05Z")));
            bool acquired = await election.TryAcquireOrRenewAsync();

            Assert.That(acquired, Is.True);
            Assert.That(lease.Spec.HolderIdentity, Is.EqualTo("pod-a"));
        }

        [Test]
        public async Task StartIsIdempotentAndDisposeStopsRenewLoopAsync()
        {
            var api = new Mock<IKubernetesApiClient>(MockBehavior.Strict);
            api.SetupGet(x => x.IsInCluster).Returns(false);
            var options = NewOptions();
            options.RenewInterval = TimeSpan.FromMilliseconds(1);

            var election = new KubernetesLeaseLeaderElection(api.Object, options, new FakeTimeProvider());
            election.Start();
            election.Start();

            await election.DisposeAsync();

            Assert.That(election.IsLeader, Is.False);
        }

        [Test]
        public async Task DisposeIgnoresReleaseFailureAsync()
        {
            var api = NewApi();
            api.Setup(x => x.GetLeaseAsync("ns", "opcua", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("transient"));

            var election = new KubernetesLeaseLeaderElection(api.Object, NewOptions(), new FakeTimeProvider());

            Assert.DoesNotThrowAsync(async () => await election.DisposeAsync().AsTask());
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
    }
}