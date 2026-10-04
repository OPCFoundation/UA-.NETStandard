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
    /// End-to-end HTTP probe tests for <see cref="KubernetesReadinessServer"/>. Each test starts the server on a
    /// loopback port of its own (see <see cref="ReadinessTestServer"/>), drives the readiness and liveness
    /// endpoints over real HTTP, and disposes the listener.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class KubernetesReadinessServerHttpTests
    {
        [Test]
        public async Task ReadinessProbeReturnsOkWhenServiceLevelMeetsMinimumAsync()
        {
            await using ReadinessTestServer readiness = await StartServerAsync(255).ConfigureAwait(false);

            (HttpStatusCode status, string body) = await GetAsync(readiness.Port, "readyz").ConfigureAwait(false);

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.EqualTo("ok"));
        }

        [Test]
        public async Task ReadinessProbeReturnsServiceUnavailableWhenBelowMinimumAsync()
        {
            await using ReadinessTestServer readiness = await StartServerAsync(0).ConfigureAwait(false);

            (HttpStatusCode status, string body) = await GetAsync(readiness.Port, "readyz").ConfigureAwait(false);

            Assert.That(status, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(body, Is.EqualTo("not ready"));
        }

        [Test]
        public async Task LivenessProbeAlwaysReturnsOkAsync()
        {
            await using ReadinessTestServer readiness = await StartServerAsync(0).ConfigureAwait(false);

            (HttpStatusCode status, string body) = await GetAsync(readiness.Port, "livez").ConfigureAwait(false);

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.EqualTo("ok"));
        }

        [Test]
        public async Task StartIsIdempotentAndDisposeStopsListenerAsync()
        {
            ReadinessTestServer readiness = await StartServerAsync(255).ConfigureAwait(false);
            readiness.Server.Start();

            (HttpStatusCode status, _) = await GetAsync(readiness.Port, "readyz").ConfigureAwait(false);
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK));

            await readiness.Server.DisposeAsync().ConfigureAwait(false);
            await readiness.Server.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task StartFailsWhenAnotherServerRegisteredTheSamePrefixAsync()
        {
            // Two readiness servers on one port register the same URL prefixes:
            // the second start is rejected (ERROR_ALREADY_EXISTS from http.sys on
            // Windows) and must leave the first server serving.
            await using ReadinessTestServer occupant = await StartServerAsync(255).ConfigureAwait(false);
            var second = new KubernetesReadinessServer(
                new ConstantServiceLevelProvider(255),
                NewOptions(occupant.Port));

            Assert.That(second.Start, Throws.TypeOf<HttpListenerException>());
            await second.DisposeAsync().ConfigureAwait(false);

            (HttpStatusCode status, _) = await GetAsync(occupant.Port, "readyz").ConfigureAwait(false);
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task StartMovesToAFreshPortWhenThePrefixIsAlreadyRegisteredAsync()
        {
            await using ReadinessTestServer occupant = await StartServerAsync(255).ConfigureAwait(false);
            int attempts = 0;

            // The first attempt is pointed at the occupied port, as if another
            // process had taken the probed port before the start.
            await using ReadinessTestServer readiness = await ReadinessTestServer.StartAsync(
                port => new KubernetesReadinessServer(
                    new ConstantServiceLevelProvider(255),
                    NewOptions(++attempts == 1 ? occupant.Port : port))).ConfigureAwait(false);

            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(readiness.Port, Is.Not.EqualTo(occupant.Port));
            (HttpStatusCode status, _) = await GetAsync(readiness.Port, "readyz").ConfigureAwait(false);
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public void UniqueFreePortsAreNeverHandedOutTwice()
        {
            var ports = new int[32];

            Parallel.For(0, ports.Length, i => ports[i] = ReadinessTestServer.GetUniqueFreePort());

            Assert.That(ports, Is.Unique);
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
        public void DisposingAnUnstartedServerDoesNotBindItsPort()
        {
            // Another process holds the port on the wildcard address, where the
            // default "+" host binds. A server that was never started must not
            // try to bind the port when it is disposed.
            var occupant = new TcpListener(Socket.OSSupportsIPv6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
            if (Socket.OSSupportsIPv6)
            {
                occupant.Server.DualMode = true;
            }
            occupant.Start();
            try
            {
                int port = ((IPEndPoint)occupant.LocalEndpoint).Port;
                var server = new KubernetesReadinessServer(
                    new ConstantServiceLevelProvider(255),
                    new KubernetesReadinessOptions { Port = port });

                Assert.That(async () => await server.DisposeAsync().ConfigureAwait(false), Throws.Nothing);
            }
            finally
            {
                occupant.Stop();
            }
        }

        [Test]
        public async Task StartAfterDisposeIsRejectedAsync()
        {
            var server = new KubernetesReadinessServer(
                new ConstantServiceLevelProvider(255),
                NewOptions(ReadinessTestServer.GetUniqueFreePort()));
            await server.DisposeAsync().ConfigureAwait(false);

            Assert.That(server.Start, Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void DefaultPortStaysOffCommonWebApplicationPorts()
        {
            var options = new KubernetesReadinessOptions();

            Assert.That(options.Port, Is.EqualTo(KubernetesReadinessOptions.DefaultPort));
            Assert.That(options.Port, Is.EqualTo(4852));
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

        private static Task<ReadinessTestServer> StartServerAsync(byte serviceLevel)
        {
            return ReadinessTestServer.StartAsync(
                port => new KubernetesReadinessServer(
                    new ConstantServiceLevelProvider(serviceLevel),
                    NewOptions(port)));
        }

        private static async Task<(HttpStatusCode Status, string Body)> GetAsync(int port, string path)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using HttpResponseMessage response = await client
                .GetAsync(new Uri($"http://localhost:{port}/{path}"))
                .ConfigureAwait(false);
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
    }
}
