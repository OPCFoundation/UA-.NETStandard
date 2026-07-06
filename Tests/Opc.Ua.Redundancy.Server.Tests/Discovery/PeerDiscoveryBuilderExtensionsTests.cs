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

#pragma warning disable CA2007

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for the peer-discovery registration extensions and the refresh-loop startup task: one call
    /// registers the discovery mechanism, the mutable redundant server set, the gossip peer sink, and the
    /// refresh loop; and discovered peers flow to the server set and the gossip fabric at runtime.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class PeerDiscoveryBuilderExtensionsTests
    {
        [Test]
        public void UsePeerDiscoveryThrowsOnNullBuilder()
        {
            Assert.That(
                () => PeerDiscoveryBuilderExtensions.UsePeerDiscovery(null!, _ => new StaticPeerDiscovery([])),
                Throws.ArgumentNullException);
        }

        [Test]
        public void UsePeerDiscoveryThrowsOnNullFactory()
        {
            var builder = new DiTestServerBuilder();

            Assert.That(() => builder.UsePeerDiscovery(null!), Throws.ArgumentNullException);
        }

        [Test]
        public async Task UsePeerDiscoveryRegistersServicesAsync()
        {
            var builder = new DiTestServerBuilder();

            builder.UseStaticPeerDiscovery([new DiscoveredPeer(
                "urn:a", new[] { "opc.tcp://a:4840" }, new IPEndPoint(IPAddress.Loopback, 4840))]);

            await using ServiceProvider sp = builder.Services.BuildServiceProvider();

            Assert.Multiple(() =>
            {
                Assert.That(sp.GetRequiredService<IPeerDiscovery>(), Is.InstanceOf<StaticPeerDiscovery>());
                Assert.That(
                    sp.GetRequiredService<IRedundantServerSetProvider>(),
                    Is.InstanceOf<DiscoveredRedundantServerSetProvider>());
                Assert.That(sp.GetRequiredService<IGossipPeerSink>(), Is.InstanceOf<GossipPeerRegistry>());
                Assert.That(
                    sp.GetServices<IServerStartupTask>().ToArray(),
                    Has.Some.InstanceOf<PeerDiscoveryStartupTask>());
            });
        }

        [Test]
        public void UseStaticPeerDiscoveryThrowsOnNullPeers()
        {
            var builder = new DiTestServerBuilder();

            Assert.That(() => builder.UseStaticPeerDiscovery(null!), Throws.ArgumentNullException);
        }

        [Test]
        public async Task UseDnsPeerDiscoveryRegistersDnsDiscoveryAsync()
        {
            var builder = new DiTestServerBuilder();

            builder.UseDnsPeerDiscovery(dns => dns.HostName = "servers.headless");

            await using ServiceProvider sp = builder.Services.BuildServiceProvider();
            Assert.That(sp.GetRequiredService<IPeerDiscovery>(), Is.InstanceOf<DnsPeerDiscovery>());
        }

        [Test]
        public void UseDnsPeerDiscoveryThrowsOnNullConfigure()
        {
            var builder = new DiTestServerBuilder();

            Assert.That(() => builder.UseDnsPeerDiscovery(null!), Throws.ArgumentNullException);
        }

        [Test]
        public async Task StartupTaskAppliesDiscoveredPeersToProviderAndSinkAsync()
        {
            var peer1 = new DiscoveredPeer(
                "urn:a", new[] { "opc.tcp://a:4840" }, new IPEndPoint(IPAddress.Loopback, 4840));
            var peer2 = new DiscoveredPeer(
                "urn:b", new[] { "opc.tcp://b:4840" }, new IPEndPoint(IPAddress.Loopback, 4842));
            var discovery = new StaticPeerDiscovery([peer1, peer2]);
            var provider = new DiscoveredRedundantServerSetProvider();
            var registry = new GossipPeerRegistry();
            var options = new PeerDiscoveryOptions { RefreshInterval = TimeSpan.FromMilliseconds(50) };

            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());

            await using (var task = new PeerDiscoveryStartupTask(discovery, provider, options, registry))
            {
                await task.OnServerStartedAsync(server.Object);
                await WaitForAsync(() => provider.GetRedundantServerSet().Count == 2, TimeSpan.FromSeconds(5));
            }

            Assert.That(provider.GetRedundantServerSet().Count, Is.EqualTo(2));

            var received = new List<IPEndPoint>();
            registry.Register(received.Add);
            Assert.That(received, Has.Count.EqualTo(2));
        }

        private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }
                await Task.Delay(25);
            }
            Assert.Fail("Condition was not met within the timeout.");
        }
    }
}
