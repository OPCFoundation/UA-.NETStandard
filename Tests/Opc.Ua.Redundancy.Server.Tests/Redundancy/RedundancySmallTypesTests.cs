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

#nullable enable

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
