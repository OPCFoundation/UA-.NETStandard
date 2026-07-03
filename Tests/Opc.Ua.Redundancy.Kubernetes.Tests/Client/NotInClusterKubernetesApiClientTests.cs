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

            KubernetesLease? lease = await client.GetLeaseAsync("ns", "opcua", CancellationToken.None);

            Assert.That(lease, Is.Null);
        }

        [Test]
        public void CreateLeaseThrowsInvalidOperation()
        {
            var client = new NotInClusterKubernetesApiClient();

            Assert.That(
                async () => await client.CreateLeaseAsync("ns", new KubernetesLease(), CancellationToken.None),
                Throws.InvalidOperationException);
        }

        [Test]
        public void ReplaceLeaseThrowsInvalidOperation()
        {
            var client = new NotInClusterKubernetesApiClient();

            Assert.That(
                async () => await client.ReplaceLeaseAsync("ns", "opcua", new KubernetesLease(), CancellationToken.None),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task DeleteLeaseCompletesWithoutThrowingAsync()
        {
            var client = new NotInClusterKubernetesApiClient();

            await client.DeleteLeaseAsync("ns", "opcua", CancellationToken.None);

            Assert.That(client.IsInCluster, Is.False);
        }

        [Test]
        public async Task ListEndpointSlicesReturnsEmptyListAsync()
        {
            var client = new NotInClusterKubernetesApiClient();

            KubernetesEndpointSliceList list = await client.ListEndpointSlicesAsync("ns", "svc", CancellationToken.None);

            Assert.That(list.Items, Is.Empty);
        }
    }
}
