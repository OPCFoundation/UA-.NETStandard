/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

#pragma warning disable CA2007

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy.Server;

namespace Opc.Ua.Redundancy.Kubernetes.Tests
{
    /// <summary>
    /// Unit tests for <see cref="KubernetesPeerDiscoveryAdapter"/>: it converts Kubernetes ServerUris to the
    /// generic <see cref="DiscoveredPeer"/> seam and forwards change notifications.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class KubernetesPeerDiscoveryAdapterTests
    {
        [Test]
        public void RejectsNullInner()
        {
            Assert.That(() => new KubernetesPeerDiscoveryAdapter(null!), Throws.ArgumentNullException);
        }

        [Test]
        public async Task RefreshConvertsServerUrisToPeersAsync()
        {
            var inner = new Mock<IKubernetesPeerDiscovery>();
            inner.Setup(d => d.RefreshAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<string>>(s_twoUris));

            var adapter = new KubernetesPeerDiscoveryAdapter(inner.Object);

            ArrayOf<DiscoveredPeer> peers = await adapter.RefreshAsync().ConfigureAwait(false);

            Assert.That(peers.Count, Is.EqualTo(2));
            Assert.That(peers[0].ServerUri, Is.EqualTo("urn:a"));
            Assert.That(peers[0].DiscoveryUrls[0], Is.EqualTo("urn:a"));
            Assert.That(peers[0].GossipEndpoint, Is.Null);
        }

        [Test]
        public void ForwardsChangeEvent()
        {
            var inner = new Mock<IKubernetesPeerDiscovery>();
            var adapter = new KubernetesPeerDiscoveryAdapter(inner.Object);
            ArrayOf<DiscoveredPeer> observed = default;
            adapter.PeersChanged += p => observed = p;

            inner.Raise(d => d.PeerServerUrisChanged += null, s_singleUri);

            Assert.That(observed.Count, Is.EqualTo(1));
            Assert.That(observed[0].ServerUri, Is.EqualTo("urn:a"));
        }

        private static readonly ArrayOf<string> s_singleUri = ["urn:a"];
        private static readonly ArrayOf<string> s_twoUris = ["urn:a", "urn:b"];
    }
}
