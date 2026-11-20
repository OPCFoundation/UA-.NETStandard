// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Redundancy.Kubernetes.Tests
{
    /// <summary>
    /// Unit tests for the not-in-cluster fallback API client that keeps the redundancy features inert when
    /// the server does not run inside Kubernetes.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class NotInClusterKubernetesApiClientTests
    {
        [Test]
        public void IsInClusterIsFalse()
        {
            var client = new NotInClusterKubernetesApiClient();

            Assert.That(client.IsInCluster, Is.False);
        }

        [Test]
        public async Task GetLeaseReturnsNullAsync()
        {
            var client = new NotInClusterKubernetesApiClient();

            KubernetesLease? lease = await client.GetLeaseAsync("ns", "opcua", CancellationToken.None).ConfigureAwait(false);

            Assert.That(lease, Is.Null);
        }

        [Test]
        public void CreateLeaseThrowsInvalidOperation()
        {
            var client = new NotInClusterKubernetesApiClient();

            Assert.That(
                async () => await client.CreateLeaseAsync("ns", new KubernetesLease(), CancellationToken.None).ConfigureAwait(false),
                Throws.InvalidOperationException);
        }

        [Test]
        public void ReplaceLeaseThrowsInvalidOperation()
        {
            var client = new NotInClusterKubernetesApiClient();

            Assert.That(
                async () => await client.ReplaceLeaseAsync("ns", "opcua", new KubernetesLease(), CancellationToken.None).ConfigureAwait(false),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task DeleteLeaseCompletesWithoutThrowingAsync()
        {
            var client = new NotInClusterKubernetesApiClient();

            await client.DeleteLeaseAsync("ns", "opcua", CancellationToken.None).ConfigureAwait(false);

            Assert.That(client.IsInCluster, Is.False);
        }

        [Test]
        public async Task ListEndpointSlicesReturnsEmptyListAsync()
        {
            var client = new NotInClusterKubernetesApiClient();

            KubernetesEndpointSliceList list = await client.ListEndpointSlicesAsync("ns", "svc", CancellationToken.None).ConfigureAwait(false);

            Assert.That(list.Items, Is.Empty);
        }
    }
}
