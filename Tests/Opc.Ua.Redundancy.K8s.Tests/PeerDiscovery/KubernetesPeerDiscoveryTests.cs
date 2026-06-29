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

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

using Opc.Ua.Redundancy.Server;

namespace Opc.Ua.Redundancy.K8s.Tests
{
    /// <summary>
    /// Unit tests for Kubernetes peer discovery.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class KubernetesPeerDiscoveryTests
    {
        private static readonly string[] ExpectedPeerUris = ["opc.tcp://10.0.0.2:4840"];

        [Test]
        public void EndpointSlicesConvertReadyAddressesToPeerUris()
        {
            KubernetesEndpointSliceList slices = NewSlices();
            var options = NewOptions();

            ArrayOf<string> peers = KubernetesPeerDiscovery.ToPeerUris(slices, options);

            Assert.That(peers.Span.ToArray(), Is.EqualTo(ExpectedPeerUris));
        }

        [Test]
        public async Task RefreshPublishesChangedPeersAsync()
        {
            var api = new Mock<IKubernetesApiClient>(MockBehavior.Strict);
            api.SetupGet(x => x.IsInCluster).Returns(true);
            api.Setup(x => x.ListEndpointSlicesAsync("ns", "svc", It.IsAny<CancellationToken>()))
                .ReturnsAsync(NewSlices());
            var discovery = new KubernetesPeerDiscovery(api.Object, NewOptions());
            ArrayOf<string> observed = ArrayOf<string>.Empty;
            discovery.PeerServerUrisChanged += peers => observed = peers;

            ArrayOf<string> refreshed = await discovery.RefreshAsync();

            Assert.That(refreshed.Span.ToArray(), Is.EqualTo(ExpectedPeerUris));
            Assert.That(observed.Span.ToArray(), Is.EqualTo(ExpectedPeerUris));
        }

        [Test]
        public async Task NotInClusterRefreshReturnsEmptyAsync()
        {
            var api = new Mock<IKubernetesApiClient>(MockBehavior.Strict);
            api.SetupGet(x => x.IsInCluster).Returns(false);
            var discovery = new KubernetesPeerDiscovery(api.Object, NewOptions());

            ArrayOf<string> refreshed = await discovery.RefreshAsync();

            Assert.That(refreshed.Count, Is.Zero);
        }

        [Test]
        public void EndpointSlicesUseFallbackPortWhenNoPortsExist()
        {
            var options = NewOptions();
            options.Port = 4841;
            KubernetesEndpointSliceList slices = NewSlices();
            slices.Items[0].Ports.Clear();

            ArrayOf<string> peers = KubernetesPeerDiscovery.ToPeerUris(slices, options);

            Assert.That(peers.Span.ToArray(), Is.EqualTo(new[] { "opc.tcp://10.0.0.2:4841" }));
        }

        [Test]
        public void EndpointSlicesUseFirstPortWhenNamedPortMissing()
        {
            var options = NewOptions();
            options.PortName = "missing";
            KubernetesEndpointSliceList slices = NewSlices();

            ArrayOf<string> peers = KubernetesPeerDiscovery.ToPeerUris(slices, options);

            Assert.That(peers.Span.ToArray(), Is.EqualTo(ExpectedPeerUris));
        }

        [Test]
        public async Task RefreshWithSamePeersDoesNotRaiseChangedEventAgainAsync()
        {
            var api = new Mock<IKubernetesApiClient>(MockBehavior.Strict);
            api.SetupGet(x => x.IsInCluster).Returns(true);
            api.Setup(x => x.ListEndpointSlicesAsync("ns", "svc", It.IsAny<CancellationToken>()))
                .ReturnsAsync(NewSlices());
            var discovery = new KubernetesPeerDiscovery(api.Object, NewOptions());
            int changedCount = 0;
            discovery.PeerServerUrisChanged += _ => changedCount++;

            await discovery.RefreshAsync();
            await discovery.RefreshAsync();

            Assert.That(changedCount, Is.EqualTo(1));
        }

        [Test]
        public async Task PopulateCopiesCurrentPeerUrisAsync()
        {
            var api = new Mock<IKubernetesApiClient>(MockBehavior.Strict);
            api.SetupGet(x => x.IsInCluster).Returns(true);
            api.Setup(x => x.ListEndpointSlicesAsync("ns", "svc", It.IsAny<CancellationToken>()))
                .ReturnsAsync(NewSlices());
            var discovery = new KubernetesPeerDiscovery(api.Object, NewOptions());
            var options = new ServerRedundancyOptions();

            await discovery.RefreshAsync();
            discovery.Populate(options);

            Assert.That(options.PeerServerUris.ToArray(), Is.EqualTo(ExpectedPeerUris));
        }

        [Test]
        public void PopulateRejectsNullOptions()
        {
            var api = new Mock<IKubernetesApiClient>(MockBehavior.Strict);
            api.SetupGet(x => x.IsInCluster).Returns(false);
            var discovery = new KubernetesPeerDiscovery(api.Object, NewOptions());

            Assert.Throws<ArgumentNullException>(() => discovery.Populate(null!));
        }

        [Test]
        public void EndpointSlicesRejectNullInputs()
        {
            Assert.Throws<ArgumentNullException>(() => KubernetesPeerDiscovery.ToPeerUris(null!, NewOptions()));
            Assert.Throws<ArgumentNullException>(() => KubernetesPeerDiscovery.ToPeerUris(NewSlices(), null!));
        }

        private static KubernetesPeerDiscoveryOptions NewOptions()
        {
            var options = new KubernetesPeerDiscoveryOptions
            {
                ServiceName = "svc",
                LocalAddress = "10.0.0.1"
            };
            options.Kubernetes.Namespace = "ns";
            return options;
        }

        private static KubernetesEndpointSliceList NewSlices()
        {
            return new KubernetesEndpointSliceList
            {
                Items =
                [
                    new KubernetesEndpointSlice
                    {
                        Ports =
                        [
                            new KubernetesEndpointPort
                            {
                                Name = "opcua-tcp",
                                Port = 4840
                            }
                        ],
                        Endpoints =
                        [
                            new KubernetesEndpoint
                            {
                                Addresses = ["10.0.0.1"]
                            },
                            new KubernetesEndpoint
                            {
                                Addresses = ["10.0.0.2"]
                            },
                            new KubernetesEndpoint
                            {
                                Addresses = ["10.0.0.3"],
                                Conditions = new KubernetesEndpointConditions
                                {
                                    Ready = false
                                }
                            }
                        ]
                    }
                ]
            };
        }
    }
}