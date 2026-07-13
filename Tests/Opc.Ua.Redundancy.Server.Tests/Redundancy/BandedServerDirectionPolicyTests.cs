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

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy.Server;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="BandedServerDirectionPolicy"/>.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class BandedServerDirectionPolicyTests
    {
        [Test]
        public async Task ServesSelfWhenLocalIsLeastLoadedInTopTierAsync()
        {
            var policy = new BandedServerDirectionPolicy(
                new FakeView([Peer("B", 255, 80)]), new LoadDirectionOptions(), _ => 0);

            string? target = await policy.SelectTargetServerUriAsync("A", 255, 10).ConfigureAwait(false);

            Assert.That(target, Is.Null, "the local Server is least-loaded among equally-healthy peers");
        }

        [Test]
        public async Task RedirectsToLeastLoadedPeerInSameTierAsync()
        {
            var policy = new BandedServerDirectionPolicy(
                new FakeView([Peer("B", 255, 10)]), new LoadDirectionOptions(), _ => 0);

            string? target = await policy.SelectTargetServerUriAsync("A", 255, 80).ConfigureAwait(false);

            Assert.That(target, Is.EqualTo("B"));
        }

        [Test]
        public async Task RedirectsToHealthierPeerRegardlessOfLoadAsync()
        {
            // Active/passive: local standby is NoData, the active peer is Healthy.
            var policy = new BandedServerDirectionPolicy(
                new FakeView([Peer("B", 255, 200)]), new LoadDirectionOptions(), _ => 0);

            string? target = await policy.SelectTargetServerUriAsync("A", ServiceLevels.NoData, 0).ConfigureAwait(false);

            Assert.That(target, Is.EqualTo("B"), "a strictly-healthier peer wins even when heavily loaded");
        }

        [Test]
        public async Task ServesSelfWhenLocalIsStrictlyHealthiestAsync()
        {
            var policy = new BandedServerDirectionPolicy(
                new FakeView([Peer("B", 100, 0)]), new LoadDirectionOptions(), _ => 0);

            string? target = await policy.SelectTargetServerUriAsync("A", 255, 90).ConfigureAwait(false);

            Assert.That(target, Is.Null, "no peer is in a higher health tier than the healthy local Server");
        }

        [Test]
        public async Task IgnoresLowerTierPeersForLoadBalancingAsync()
        {
            var policy = new BandedServerDirectionPolicy(
                new FakeView([Peer("B", 255, 10), Peer("C", 100, 0)]), new LoadDirectionOptions(), _ => 0);

            string? target = await policy.SelectTargetServerUriAsync("A", 255, 90).ConfigureAwait(false);

            Assert.That(target, Is.EqualTo("B"), "only the top health tier participates; the degraded peer is ignored");
        }

        [Test]
        public async Task RandomTieBreakSelectsWithinEqualLoadBandAsync()
        {
            ArrayOf<PeerDirectionRecord> peers = [Peer("B", 255, 0), Peer("C", 255, 5)];

            var pickFirst = new BandedServerDirectionPolicy(new FakeView(peers), new LoadDirectionOptions(), _ => 0);
            var pickSecond = new BandedServerDirectionPolicy(new FakeView(peers), new LoadDirectionOptions(), _ => 1);

            string? first = await pickFirst.SelectTargetServerUriAsync("A", 255, 100).ConfigureAwait(false);
            string? second = await pickSecond.SelectTargetServerUriAsync("A", 255, 100).ConfigureAwait(false);

            Assert.That(first, Is.EqualTo("B"));
            Assert.That(second, Is.EqualTo("C"), "B and C share the lowest load band; the selector breaks the tie");
        }

        [Test]
        public async Task PeerWithUnknownLoadIsDeprioritizedAsync()
        {
            var policy = new BandedServerDirectionPolicy(
                new FakeView([Peer("B", 255, load: null)]), new LoadDirectionOptions(), _ => 0);

            string? target = await policy.SelectTargetServerUriAsync("A", 255, 50).ConfigureAwait(false);

            Assert.That(target, Is.Null, "a peer with an unknown load must not be preferred over the known local load");
        }

        [Test]
        public async Task EmptyViewServesSelfAsync()
        {
            var policy = new BandedServerDirectionPolicy(
                new FakeView([]), new LoadDirectionOptions(), _ => 0);

            string? target = await policy.SelectTargetServerUriAsync("A", 255, 0).ConfigureAwait(false);

            Assert.That(target, Is.Null);
        }

        [Test]
        public async Task ViewFailureServesSelfAsync()
        {
            var policy = new BandedServerDirectionPolicy(
                new FakeView([], throwOnRead: true), new LoadDirectionOptions(), _ => 0);

            string? target = await policy.SelectTargetServerUriAsync("A", 255, 0).ConfigureAwait(false);

            Assert.That(target, Is.Null, "a stale/unreadable peer view must fail safe to the local Server");
        }

        [Test]
        public async Task HealthSubBandsPreferHigherHealthySubBandAsync()
        {
            // Local (210) has a strictly higher raw ServiceLevel than peer B (205), but a heavier
            // load. Without sub-banding both fall into the same coarse Healthy tier, so the tie is
            // broken by load and B (unloaded) wins. With a sub-band size of 10, local's ServiceLevel
            // (210) falls one sub-band above B's (205), so local becomes the strictly healthier
            // member and is kept regardless of its higher load.
            var withoutSubBands = new BandedServerDirectionPolicy(
                new FakeView([Peer("B", 205, 0)]),
                new LoadDirectionOptions { HealthSubBandSize = 0 },
                _ => 0);
            var withSubBands = new BandedServerDirectionPolicy(
                new FakeView([Peer("B", 205, 0)]),
                new LoadDirectionOptions { HealthSubBandSize = 10 },
                _ => 0);

            string? withoutSubBandsTarget = await withoutSubBands
                .SelectTargetServerUriAsync("A", 210, 100)
                .ConfigureAwait(false);
            string? withSubBandsTarget = await withSubBands
                .SelectTargetServerUriAsync("A", 210, 100)
                .ConfigureAwait(false);

            Assert.That(withoutSubBandsTarget, Is.EqualTo("B"), "without sub-bands the tie is broken by load, and B is unloaded");
            Assert.That(withSubBandsTarget, Is.Null, "a higher healthy sub-band should keep traffic on the healthier local server");
        }

        [TestCase(0, 18, 17, Description = "load band size is clamped to one")]
        [TestCase(1, 18, 17, Description = "load band size one keeps exact ordering")]
        [TestCase(256, 18, 17, Description = "large load bands collapse nearby loads into one band")]
        public async Task LoadBandSizeOptionsAreAppliedSafelyAsync(int loadBandSize, byte localLoad, byte peerLoad)
        {
            // localLoad (18) is strictly heavier than peerLoad (17): with an exact (or clamped-to-one)
            // band size the peer is the truly least-loaded member and should be selected; a large band
            // size collapses both into the same band, so the tie-break keeps traffic local.
            var policy = new BandedServerDirectionPolicy(
                new FakeView([Peer("B", 255, peerLoad)]),
                new LoadDirectionOptions { LoadBandSize = loadBandSize },
                _ => 0);

            string? target = await policy
                .SelectTargetServerUriAsync("A", 255, localLoad)
                .ConfigureAwait(false);

            if (loadBandSize > 1)
            {
                Assert.That(target, Is.Null, "equal load bands should keep traffic local when tie-breaking picks self");
            }
            else
            {
                Assert.That(target, Is.EqualTo("B"), "smaller load bands should still prefer the truly least-loaded peer");
            }
        }

        private static PeerDirectionRecord Peer(string uri, byte level, byte? load = null)
        {
            return new PeerDirectionRecord
            {
                ServerUri = uri,
                ServiceLevel = level,
                LoadWeight = load ?? 0,
                LoadKnown = load.HasValue
            };
        }

        private sealed class FakeView : IPeerDirectionView
        {
            public FakeView(ArrayOf<PeerDirectionRecord> peers, bool throwOnRead = false)
            {
                m_peers = peers;
                m_throw = throwOnRead;
            }

            public ValueTask<ArrayOf<PeerDirectionRecord>> GetPeersAsync(CancellationToken cancellationToken = default)
            {
                if (m_throw)
                {
                    throw new InvalidOperationException("simulated store failure");
                }
                return new ValueTask<ArrayOf<PeerDirectionRecord>>(m_peers);
            }

            private readonly ArrayOf<PeerDirectionRecord> m_peers;
            private readonly bool m_throw;
        }
    }
}
