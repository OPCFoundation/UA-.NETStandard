/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy.Server;
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
                "urn:a", discoveryUrlsA, new IPEndPoint(IPAddress.Loopback, 4840))]);

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
                "urn:a", discoveryUrlsA, new IPEndPoint(IPAddress.Loopback, 4840));
            var peer2 = new DiscoveredPeer(
                "urn:b", discoveryUrlsB, new IPEndPoint(IPAddress.Loopback, 4842));
            var discovery = new StaticPeerDiscovery([peer1, peer2]);
            var provider = new DiscoveredRedundantServerSetProvider();
            var registry = new GossipPeerRegistry();
            var options = new PeerDiscoveryOptions { RefreshInterval = TimeSpan.FromMilliseconds(50) };

            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            server.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));

            await using (var task = new PeerDiscoveryStartupTask(discovery, provider, options, registry))
            {
                await task.OnServerStartedAsync(server.Object).ConfigureAwait(false);
                await WaitForAsync(() => provider.GetRedundantServerSet().Count == 2, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }

            Assert.That(provider.GetRedundantServerSet().Count, Is.EqualTo(2));

            var received = new List<IPEndPoint>();
            registry.Register(received.Add);
            Assert.That(received, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task StartupTaskContinuesAfterRefreshErrorAsync()
        {
            var discovery = new ThrowingPeerDiscovery();
            var provider = new DiscoveredRedundantServerSetProvider();
            var options = new PeerDiscoveryOptions { RefreshInterval = TimeSpan.FromMilliseconds(20) };

            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            server.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));

            await using (var task = new PeerDiscoveryStartupTask(discovery, provider, options))
            {
                await task.OnServerStartedAsync(server.Object).ConfigureAwait(false);
                await Task.Delay(80).ConfigureAwait(false);
            }

            // The refresh error is caught and logged; the provider stays empty and the task disposes cleanly.
            Assert.That(provider.GetRedundantServerSet().Count, Is.Zero);
            Assert.That(discovery.Attempts, Is.GreaterThan(0));
        }

        [Test]
        public async Task StartupTaskWithoutSinkUpdatesProviderAsync()
        {
            var discovery = new StaticPeerDiscovery(
                [new DiscoveredPeer("urn:a", discoveryUrlsA)]);
            var provider = new DiscoveredRedundantServerSetProvider();
            var options = new PeerDiscoveryOptions { RefreshInterval = TimeSpan.FromMilliseconds(50) };

            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            server.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));

            await using (var task = new PeerDiscoveryStartupTask(discovery, provider, options, gossipSink: null))
            {
                await task.OnServerStartedAsync(server.Object).ConfigureAwait(false);
                await WaitForAsync(() => provider.GetRedundantServerSet().Count == 1, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }

            Assert.That(provider.GetRedundantServerSet().Count, Is.EqualTo(1));
        }

        [Test]
        public async Task UseLdsPeerDiscoveryRegistersLdsDiscoveryAsync()
        {
            var builder = new DiTestServerBuilder();

            builder.UseLdsPeerDiscovery(
                _ => ct => new ValueTask<ArrayOf<ApplicationDescription>>(default(ArrayOf<ApplicationDescription>)),
                lds => lds.LocalApplicationUri = "urn:self");

            await using ServiceProvider sp = builder.Services.BuildServiceProvider();
            Assert.That(sp.GetRequiredService<IPeerDiscovery>(), Is.InstanceOf<LdsPeerDiscovery>());
        }

        [Test]
        public void UseLdsPeerDiscoveryThrowsOnNullFindServers()
        {
            var builder = new DiTestServerBuilder();

            Assert.That(() => builder.UseLdsPeerDiscovery(null!), Throws.ArgumentNullException);
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
                await Task.Delay(25).ConfigureAwait(false);
            }
            Assert.Fail("Condition was not met within the timeout.");
        }

        private sealed class ThrowingPeerDiscovery : IPeerDiscovery
        {
            public event Action<ArrayOf<DiscoveredPeer>>? PeersChanged;

            public ArrayOf<DiscoveredPeer> Peers => default;

            public int Attempts => Volatile.Read(ref m_attempts);

            public ValueTask<ArrayOf<DiscoveredPeer>> RefreshAsync(CancellationToken ct = default)
            {
                Interlocked.Increment(ref m_attempts);
                PeersChanged?.Invoke(default);
                throw new InvalidOperationException("discovery failure");
            }

            private int m_attempts;
        }

        private static readonly string[] discoveryUrlsA = ["opc.tcp://a:4840"];
        private static readonly string[] discoveryUrlsB = ["opc.tcp://b:4840"];
    }
}
