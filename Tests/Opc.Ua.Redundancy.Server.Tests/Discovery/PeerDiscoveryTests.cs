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

using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server.Tests
{
    /// <summary>
    /// Unit tests for the dynamic peer-discovery seam: the static fallback and DNS mechanisms, the mutable
    /// redundant server-set provider, and the runtime gossip peer sink.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class PeerDiscoveryTests
    {
        private static DiscoveredPeer Peer(string uri, int gossipPort = 4840)
        {
            return new DiscoveredPeer(uri, new[] { uri }, new IPEndPoint(IPAddress.Loopback, gossipPort));
        }

        [Test]
        public async Task StaticDiscoveryReturnsConfiguredPeersAsync()
        {
            var discovery = new StaticPeerDiscovery([Peer("urn:a"), Peer("urn:b")]);

            ArrayOf<DiscoveredPeer> peers = await discovery.RefreshAsync().ConfigureAwait(false);

            Assert.That(peers.Count, Is.EqualTo(2));
            Assert.That(discovery.Peers.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task StaticDiscoveryRaisesChangedOnceForUnchangedSetAsync()
        {
            var discovery = new StaticPeerDiscovery([Peer("urn:a")]);
            int changes = 0;
            discovery.PeersChanged += _ => Interlocked.Increment(ref changes);

            await discovery.RefreshAsync().ConfigureAwait(false);
            await discovery.RefreshAsync().ConfigureAwait(false);

            Assert.That(changes, Is.EqualTo(1));
        }

        [Test]
        public void StaticDiscoveryRejectsNullPeers()
        {
            Assert.That(() => new StaticPeerDiscovery(null!), Throws.ArgumentNullException);
        }

        [Test]
        public async Task DnsDiscoveryBuildsPeersFromResolvedAddressesAsync()
        {
            var options = new DnsPeerDiscoveryOptions
            {
                HostName = "servers.headless",
                GossipPort = 4840,
                ApplicationPort = 4841
            };
            var discovery = new DnsPeerDiscovery(
                options,
                (host, ct) => new ValueTask<IPAddress[]>(
                    [IPAddress.Parse("10.0.0.1"), IPAddress.Parse("10.0.0.2")]),
                ct => new ValueTask<ISet<IPAddress>>(new HashSet<IPAddress>()));

            ArrayOf<DiscoveredPeer> peers = await discovery.RefreshAsync().ConfigureAwait(false);

            Assert.That(peers.Count, Is.EqualTo(2));
            Assert.That(peers[0].GossipEndpoint!.Port, Is.EqualTo(4840));
            Assert.That(peers[0].DiscoveryUrls[0], Is.EqualTo("opc.tcp://10.0.0.1:4841"));
            Assert.That(peers[0].ServerUri, Is.EqualTo("opc.tcp://10.0.0.1:4841"));
        }

        [Test]
        public async Task DnsDiscoveryExcludesLocalAddressesAsync()
        {
            var options = new DnsPeerDiscoveryOptions { HostName = "servers.headless" };
            var discovery = new DnsPeerDiscovery(
                options,
                (host, ct) => new ValueTask<IPAddress[]>(
                    [IPAddress.Parse("10.0.0.1"), IPAddress.Parse("10.0.0.9")]),
                ct => new ValueTask<ISet<IPAddress>>(
                    new HashSet<IPAddress> { IPAddress.Parse("10.0.0.9") }));

            ArrayOf<DiscoveredPeer> peers = await discovery.RefreshAsync().ConfigureAwait(false);

            Assert.That(peers.Count, Is.EqualTo(1));
            Assert.That(peers[0].DiscoveryUrls[0], Does.Contain("10.0.0.1"));
        }

        [Test]
        public async Task DnsDiscoveryDedupsAddressesAsync()
        {
            var options = new DnsPeerDiscoveryOptions { HostName = "servers.headless" };
            var discovery = new DnsPeerDiscovery(
                options,
                (host, ct) => new ValueTask<IPAddress[]>(
                    [IPAddress.Parse("10.0.0.1"), IPAddress.Parse("10.0.0.1")]),
                ct => new ValueTask<ISet<IPAddress>>(new HashSet<IPAddress>()));

            ArrayOf<DiscoveredPeer> peers = await discovery.RefreshAsync().ConfigureAwait(false);

            Assert.That(peers.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task DnsDiscoveryEmptyHostNameReturnsEmptyAsync()
        {
            var discovery = new DnsPeerDiscovery(new DnsPeerDiscoveryOptions());

            ArrayOf<DiscoveredPeer> peers = await discovery.RefreshAsync().ConfigureAwait(false);

            Assert.That(peers.Count, Is.Zero);
        }

        [Test]
        public async Task DnsDiscoveryOmitsGossipEndpointWhenDisabledAsync()
        {
            var options = new DnsPeerDiscoveryOptions
            {
                HostName = "servers.headless",
                IncludeGossipEndpoint = false
            };
            var discovery = new DnsPeerDiscovery(
                options,
                (host, ct) => new ValueTask<IPAddress[]>([IPAddress.Parse("10.0.0.1")]),
                ct => new ValueTask<ISet<IPAddress>>(new HashSet<IPAddress>()));

            ArrayOf<DiscoveredPeer> peers = await discovery.RefreshAsync().ConfigureAwait(false);

            Assert.That(peers[0].GossipEndpoint, Is.Null);
        }

        [Test]
        public void ProviderPublishesUpdatedPeers()
        {
            var provider = new DiscoveredRedundantServerSetProvider();

            provider.Update(new[] { Peer("urn:a"), Peer("urn:b") });

            ArrayOf<ApplicationDescription> set = provider.GetRedundantServerSet();
            Assert.That(set.Count, Is.EqualTo(2));
            Assert.That(set[0].ApplicationUri, Is.EqualTo("urn:a"));
            Assert.That(set[0].ApplicationType, Is.EqualTo(ApplicationType.Server));
        }

        [Test]
        public void ProviderSkipsPeersWithoutDiscoveryUrls()
        {
            var provider = new DiscoveredRedundantServerSetProvider();

            provider.Update(new[] { new DiscoveredPeer("urn:no-urls") });

            Assert.That(provider.GetRedundantServerSet().Count, Is.Zero);
        }

        [Test]
        public void ProviderReturnsEmptyBeforeUpdate()
        {
            var provider = new DiscoveredRedundantServerSetProvider();

            Assert.That(provider.GetRedundantServerSet().Count, Is.Zero);
        }

        [Test]
        public void RegistryFansOutAndReplays()
        {
            var registry = new GossipPeerRegistry();
            var received = new List<IPEndPoint>();
            var e1 = new IPEndPoint(IPAddress.Loopback, 4840);
            var e2 = new IPEndPoint(IPAddress.Loopback, 4841);

            registry.AddPeer(e1);
            registry.Register(received.Add);
            registry.AddPeer(e2);

            Assert.That(received, Is.EquivalentTo([e1, e2]));
        }

        [Test]
        public void RegistryDedupsEndpoints()
        {
            var registry = new GossipPeerRegistry();
            var received = new List<IPEndPoint>();
            var e1 = new IPEndPoint(IPAddress.Loopback, 4840);

            registry.Register(received.Add);
            registry.AddPeer(e1);
            registry.AddPeer(e1);

            Assert.That(received, Has.Count.EqualTo(1));
        }

        [Test]
        public void RegistryRejectsNullArguments()
        {
            var registry = new GossipPeerRegistry();

            Assert.Multiple(() =>
            {
                Assert.That(() => registry.Register(null!), Throws.ArgumentNullException);
                Assert.That(() => registry.AddPeer(null!), Throws.ArgumentNullException);
                Assert.That(() => registry.AddPeers(null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public async Task LdsDiscoveryConvertsServerDescriptionsAsync()
        {
            var options = new LdsPeerDiscoveryOptions { LocalApplicationUri = "urn:self" };
            var discovery = new LdsPeerDiscovery(options, ct => new ValueTask<ArrayOf<ApplicationDescription>>(

                [
                    new ApplicationDescription
                    {
                        ApplicationUri = "urn:peer",
                        ApplicationType = ApplicationType.Server,
                        DiscoveryUrls = resultPeer
                    },
                    new ApplicationDescription
                    {
                        ApplicationUri = "urn:self",
                        ApplicationType = ApplicationType.Server,
                        DiscoveryUrls = resultSelf
                    }
                ]));

            ArrayOf<DiscoveredPeer> peers = await discovery.RefreshAsync().ConfigureAwait(false);

            Assert.That(peers.Count, Is.EqualTo(1));
            Assert.That(peers[0].ServerUri, Is.EqualTo("urn:peer"));
            Assert.That(peers[0].DiscoveryUrls[0], Is.EqualTo("opc.tcp://peer:4840"));
        }

        [Test]
        public async Task LdsDiscoveryFiltersNonServersAsync()
        {
            var discovery = new LdsPeerDiscovery(
                new LdsPeerDiscoveryOptions(),
                ct => new ValueTask<ArrayOf<ApplicationDescription>>(

                    [
                        new ApplicationDescription
                        {
                            ApplicationUri = "urn:client",
                            ApplicationType = ApplicationType.Client,
                            DiscoveryUrls = resultClient
                        }
                    ]));

            ArrayOf<DiscoveredPeer> peers = await discovery.RefreshAsync().ConfigureAwait(false);

            Assert.That(peers.Count, Is.Zero);
        }

        [Test]
        public void LdsDiscoveryRejectsNullFindServers()
        {
            Assert.That(
                () => new LdsPeerDiscovery(new LdsPeerDiscoveryOptions(), null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task LdsDiscoveryIncludesClientAndServerAndSkipsNullAsync()
        {
            var discovery = new LdsPeerDiscovery(
                new LdsPeerDiscoveryOptions(),
                ct => new ValueTask<ArrayOf<ApplicationDescription>>(

                    [
                        null!,
                        new ApplicationDescription
                        {
                            ApplicationUri = "urn:both",
                            ApplicationType = ApplicationType.ClientAndServer,
                            DiscoveryUrls = resultBoth
                        },
                        new ApplicationDescription
                        {
                            ApplicationUri = string.Empty,
                            ApplicationType = ApplicationType.Server
                        }
                    ]));

            ArrayOf<DiscoveredPeer> peers = await discovery.RefreshAsync().ConfigureAwait(false);

            Assert.That(peers.Count, Is.EqualTo(1));
            Assert.That(peers[0].ServerUri, Is.EqualTo("urn:both"));
        }

        [Test]
        public void RegistryAddPeersAddsAll()
        {
            var registry = new GossipPeerRegistry();
            var received = new List<IPEndPoint>();
            registry.Register(received.Add);

            registry.AddPeers(
            [
                new IPEndPoint(IPAddress.Loopback, 4840),
                new IPEndPoint(IPAddress.Loopback, 4841)
            ]);

            Assert.That(received, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task DnsDiscoveryIncludesLocalWhenExclusionDisabledAsync()
        {
            var options = new DnsPeerDiscoveryOptions
            {
                HostName = "servers.headless",
                ExcludeLocalAddresses = false
            };
            var discovery = new DnsPeerDiscovery(
                options,
                (host, ct) => new ValueTask<IPAddress[]>([IPAddress.Parse("10.0.0.9")]),
                ct => new ValueTask<ISet<IPAddress>>(
                    new HashSet<IPAddress> { IPAddress.Parse("10.0.0.9") }));

            ArrayOf<DiscoveredPeer> peers = await discovery.RefreshAsync().ConfigureAwait(false);

            Assert.That(peers.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task DnsDiscoveryBuildsBracketedIpv6UrlAsync()
        {
            var options = new DnsPeerDiscoveryOptions { HostName = "servers.headless", ApplicationPort = 4841 };
            var discovery = new DnsPeerDiscovery(
                options,
                (host, ct) => new ValueTask<IPAddress[]>([IPAddress.Parse("2001:db8::1")]),
                ct => new ValueTask<ISet<IPAddress>>(new HashSet<IPAddress>()));

            ArrayOf<DiscoveredPeer> peers = await discovery.RefreshAsync().ConfigureAwait(false);

            Assert.That(peers[0].DiscoveryUrls[0], Is.EqualTo("opc.tcp://[2001:db8::1]:4841"));
        }

        [Test]
        public async Task DnsDiscoveryDefaultDelegatesExcludeLoopbackAsync()
        {
            // No resolver/local-address overrides: exercises DefaultResolveAsync + DefaultLocalAddressesAsync.
            // "localhost" resolves to loopback, which the default local-address set excludes.
            var discovery = new DnsPeerDiscovery(new DnsPeerDiscoveryOptions { HostName = "localhost" });

            ArrayOf<DiscoveredPeer> peers = await discovery.RefreshAsync().ConfigureAwait(false);

            Assert.That(peers.Count, Is.Zero);
        }

        [Test]
        public void CompositeReturnsPrimaryWhenNonEmpty()
        {
            var primary = new FixedServerSet("urn:a", "urn:b");
            var fallback = new FixedServerSet("urn:fallback");
            var composite = new CompositeRedundantServerSetProvider(primary, fallback);

            ArrayOf<ApplicationDescription> set = composite.GetRedundantServerSet();

            Assert.That(set.Count, Is.EqualTo(2));
            Assert.That(set[0].ApplicationUri, Is.EqualTo("urn:a"));
        }

        [Test]
        public void CompositeFallsBackWhenPrimaryEmpty()
        {
            var primary = new FixedServerSet();
            var fallback = new FixedServerSet("urn:fallback");
            var composite = new CompositeRedundantServerSetProvider(primary, fallback);

            ArrayOf<ApplicationDescription> set = composite.GetRedundantServerSet();

            Assert.That(set.Count, Is.EqualTo(1));
            Assert.That(set[0].ApplicationUri, Is.EqualTo("urn:fallback"));
        }

        [Test]
        public void CompositeRejectsNullArguments()
        {
            var provider = new FixedServerSet();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new CompositeRedundantServerSetProvider(null!, provider),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => new CompositeRedundantServerSetProvider(provider, null!),
                    Throws.ArgumentNullException);
            });
        }

        private sealed class FixedServerSet : IRedundantServerSetProvider
        {
            public FixedServerSet(params string[] uris)
            {
                m_set = new ApplicationDescription[uris.Length];
                for (int i = 0; i < uris.Length; i++)
                {
                    m_set[i] = new ApplicationDescription { ApplicationUri = uris[i] };
                }
            }

            public ArrayOf<ApplicationDescription> GetRedundantServerSet()
            {
                return m_set;
            }

            private readonly ApplicationDescription[] m_set;
        }

        private static readonly string[] resultPeer = ["opc.tcp://peer:4840"];
        private static readonly string[] resultSelf = ["opc.tcp://self:4840"];
        private static readonly string[] resultClient = ["opc.tcp://client:4840"];
        private static readonly string[] resultBoth = ["opc.tcp://both:4840"];
    }
}
