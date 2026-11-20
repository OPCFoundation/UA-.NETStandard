// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using NUnit.Framework;

namespace Opc.Ua.PubSub.Eth.Tests
{
    /// <summary>
    /// Validates the defaults of <see cref="EthTransportOptions"/>.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    public sealed class EthTransportOptionsTests
    {
        [Test]
        public void DefaultsAreSafe()
        {
            var options = new EthTransportOptions();

            Assert.Multiple(() =>
            {
                Assert.That(options.ReceiveQueueCapacity, Is.EqualTo(1024));
                Assert.That(options.MaxFrameSize, Is.EqualTo(1522));
                Assert.That(options.PreferredNetworkInterface, Is.Null);
                Assert.That(options.DefaultVlanId, Is.Null);
                Assert.That(options.DefaultPriority, Is.Null);
                Assert.That(options.Promiscuous, Is.False);
                Assert.That(options.DiscoveryAnnounceRate, Is.Zero);
                Assert.That(options.DiscoveryMulticastAddress, Is.Null);
            });
        }
    }
}
