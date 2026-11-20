// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using NUnit.Framework;
using Opc.Ua.Redundancy.Server;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for the small value types and helpers of the load-direction feature:
    /// <see cref="ConstantLoadWeightProvider"/>, <see cref="LoadDirectionRandom"/> and
    /// <see cref="RedundantPeer"/>.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class RedundancySmallTypesTests
    {
        [Test]
        public void ConstantLoadWeightProviderReportsFixedWeight()
        {
            var provider = new ConstantLoadWeightProvider(77);

            Assert.That(provider.GetLoadWeight(), Is.EqualTo((byte)77));
        }

        [Test]
        public void ConstantLoadWeightProviderDefaultsToIdle()
        {
            var provider = new ConstantLoadWeightProvider();

            Assert.That(provider.GetLoadWeight(), Is.Zero);
        }

        [Test]
        public void LoadDirectionRandomReturnsZeroForAtMostOneChoice()
        {
            Assert.That(LoadDirectionRandom.NextIndex(0), Is.Zero);
            Assert.That(LoadDirectionRandom.NextIndex(1), Is.Zero);
        }

        [Test]
        public void LoadDirectionRandomStaysWithinRange()
        {
            const int exclusiveMax = 5;

            for (int i = 0; i < 200; i++)
            {
                int index = LoadDirectionRandom.NextIndex(exclusiveMax);
                Assert.That(index, Is.InRange(0, exclusiveMax - 1));
            }
        }

        [Test]
        public void RedundantPeerConstructorAssignsProperties()
        {
            ArrayOf<string> discoveryUrls = ["opc.tcp://peer:4840"];
            string[] expectedUrls = ["opc.tcp://peer:4840"];

            var peer = new RedundantPeer("urn:peer", discoveryUrls);

            Assert.That(peer.ApplicationUri, Is.EqualTo("urn:peer"));
            Assert.That(peer.DiscoveryUrls.ToArray(), Is.EqualTo(expectedUrls));
        }

        [Test]
        public void RedundantPeerDefaultsAreEmpty()
        {
            var peer = new RedundantPeer();

            Assert.That(peer.ApplicationUri, Is.Empty);
            Assert.That(peer.DiscoveryUrls.Count, Is.Zero);
        }
    }
}
