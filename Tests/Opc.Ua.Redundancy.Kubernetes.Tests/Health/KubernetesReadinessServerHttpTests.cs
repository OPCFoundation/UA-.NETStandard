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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy.Server;

namespace Opc.Ua.Redundancy.Kubernetes.Tests
{
    /// <summary>
    /// End-to-end HTTP probe tests for <see cref="KubernetesReadinessServer"/>. Each test binds an ephemeral
    /// loopback port, drives the readiness and liveness endpoints over real HTTP, and disposes the listener.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class KubernetesReadinessServerHttpTests
    {
        [Test]
        public async Task ReadinessProbeReturnsOkWhenServiceLevelMeetsMinimumAsync()
        {
            int port = GetFreePort();
            await using var server = new KubernetesReadinessServer(
                new ConstantServiceLevelProvider(255),
                NewOptions(port));
            server.Start();

            (HttpStatusCode status, string body) = await GetAsync($"http://localhost:{port}/readyz").ConfigureAwait(false);

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.EqualTo("ok"));
        }

        [Test]
        public async Task ReadinessProbeReturnsServiceUnavailableWhenBelowMinimumAsync()
        {
            int port = GetFreePort();
            await using var server = new KubernetesReadinessServer(
                new ConstantServiceLevelProvider(0),
                NewOptions(port));
            server.Start();

            (HttpStatusCode status, string body) = await GetAsync($"http://localhost:{port}/readyz").ConfigureAwait(false);

            Assert.That(status, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(body, Is.EqualTo("not ready"));
        }

        [Test]
        public async Task LivenessProbeAlwaysReturnsOkAsync()
        {
            int port = GetFreePort();
            await using var server = new KubernetesReadinessServer(
                new ConstantServiceLevelProvider(0),
                NewOptions(port));
            server.Start();

            (HttpStatusCode status, string body) = await GetAsync($"http://localhost:{port}/livez").ConfigureAwait(false);

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.EqualTo("ok"));
        }

        [Test]
        public async Task StartIsIdempotentAndDisposeStopsListenerAsync()
        {
            int port = GetFreePort();
            var server = new KubernetesReadinessServer(new ConstantServiceLevelProvider(255), NewOptions(port));
            server.Start();
            server.Start();

            (HttpStatusCode status, _) = await GetAsync($"http://localhost:{port}/readyz").ConfigureAwait(false);
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK));

            await server.DisposeAsync().ConfigureAwait(false);
            await server.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task InstanceIsReadyReflectsServiceLevelProviderAsync()
        {
            await using var readyServer = new KubernetesReadinessServer(
                new ConstantServiceLevelProvider(255),
                new KubernetesReadinessOptions());
            await using var notReadyServer = new KubernetesReadinessServer(
                new ConstantServiceLevelProvider(0),
                new KubernetesReadinessOptions());

            Assert.That(readyServer.IsReady(), Is.True);
            Assert.That(notReadyServer.IsReady(), Is.False);
        }

        [Test]
        public void ConstructorRejectsNullServiceLevelProvider()
        {
            Assert.That(
                () => new KubernetesReadinessServer(null!, new KubernetesReadinessOptions()),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorRejectsNullOptions()
        {
            Assert.That(
                () => new KubernetesReadinessServer(new ConstantServiceLevelProvider(255), null!),
                Throws.ArgumentNullException);
        }

        private static async Task<(HttpStatusCode Status, string Body)> GetAsync(string url)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using HttpResponseMessage response = await client.GetAsync(new Uri(url)).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return (response.StatusCode, body);
        }

        private static KubernetesReadinessOptions NewOptions(int port)
        {
            return new KubernetesReadinessOptions
            {
                Host = "localhost",
                Port = port
            };
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
