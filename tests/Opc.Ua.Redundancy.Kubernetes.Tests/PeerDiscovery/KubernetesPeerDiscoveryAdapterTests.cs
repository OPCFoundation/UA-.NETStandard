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
