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

// CA2000: the HttpClient and readiness server are released deterministically
// per test (using / await using); there is no cross-test resource leak.
#pragma warning disable CA2000

using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Kubernetes.Tests
{
    /// <summary>
    /// Unit tests for the readiness startup task that starts the <see cref="KubernetesReadinessServer"/> once the
    /// OPC UA server has started.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class KubernetesReadinessStartupTaskTests
    {
        [Test]
        public void ConstructorRejectsNullServer()
        {
            Assert.That(
                () => new KubernetesReadinessStartupTask(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task OnServerStartedRejectsNullServerAsync()
        {
            await using var readiness = new KubernetesReadinessServer(
                new ConstantServiceLevelProvider(255),
                new KubernetesReadinessOptions());
            var task = new KubernetesReadinessStartupTask(readiness);

            Assert.That(
                async () => await task.OnServerStartedAsync(null!).ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task OnServerStartedStartsReadinessServerAsync()
        {
            int port = GetFreePort();
            await using var readiness = new KubernetesReadinessServer(
                new ConstantServiceLevelProvider(255),
                new KubernetesReadinessOptions { Host = "localhost", Port = port });
            var task = new KubernetesReadinessStartupTask(readiness);

            await task.OnServerStartedAsync(Mock.Of<IServerInternal>(), CancellationToken.None).ConfigureAwait(false);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using HttpResponseMessage response = await client.GetAsync(new Uri($"http://localhost:{port}/readyz")).ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        private static int GetFreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            try
            {
                return ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            finally
            {
                probe.Stop();
            }
        }
    }
}
