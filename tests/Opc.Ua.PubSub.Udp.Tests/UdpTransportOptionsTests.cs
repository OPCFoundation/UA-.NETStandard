/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Opc.Ua.PubSub.Udp.Tests
{
    /// <summary>
    /// Verifies <see cref="UdpTransportOptions"/> defaults and
    /// <c>IConfiguration</c> binding round-trip used in DI wiring.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    public sealed class UdpTransportOptionsTests
    {
        [Test]
        public void Defaults_MatchSpecGuidance()
        {
            var options = new UdpTransportOptions();

            Assert.That(options.SendBufferSize, Is.EqualTo(64 * 1024));
            Assert.That(options.ReceiveBufferSize, Is.EqualTo(256 * 1024));
            Assert.That(options.ReceiveQueueCapacity, Is.EqualTo(1024));
            Assert.That(options.Ttl, Is.EqualTo(1));
            Assert.That(options.MulticastLoopback, Is.False);
            Assert.That(options.MaxFrameSize, Is.EqualTo(65507));
            Assert.That(options.MessageRepeatCount, Is.Zero);
            Assert.That(options.MessageRepeatDelay, Is.EqualTo(TimeSpan.FromMilliseconds(5)));
            Assert.That(options.PreferredNetworkInterface, Is.Null);
        }

        [Test]
        public void Defaults_PropertiesAreMutable()
        {
            var options = new UdpTransportOptions
            {
                SendBufferSize = 1024,
                ReceiveBufferSize = 2048,
                ReceiveQueueCapacity = 16,
                Ttl = 32,
                MulticastLoopback = true,
                MaxFrameSize = 512,
                MessageRepeatCount = 3,
                MessageRepeatDelay = TimeSpan.FromMilliseconds(50),
                PreferredNetworkInterface = "eth0"
            };

            Assert.That(options.SendBufferSize, Is.EqualTo(1024));
            Assert.That(options.ReceiveBufferSize, Is.EqualTo(2048));
            Assert.That(options.ReceiveQueueCapacity, Is.EqualTo(16));
            Assert.That(options.Ttl, Is.EqualTo(32));
            Assert.That(options.MulticastLoopback, Is.True);
            Assert.That(options.MaxFrameSize, Is.EqualTo(512));
            Assert.That(options.MessageRepeatCount, Is.EqualTo(3));
            Assert.That(options.MessageRepeatDelay, Is.EqualTo(TimeSpan.FromMilliseconds(50)));
            Assert.That(options.PreferredNetworkInterface, Is.EqualTo("eth0"));
        }

        [Test]
        public void IConfiguration_Binding_PopulatesAllScalarProperties()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SendBufferSize"] = "8192",
                    ["ReceiveBufferSize"] = "16384",
                    ["ReceiveQueueCapacity"] = "8",
                    ["Ttl"] = "5",
                    ["MulticastLoopback"] = "true",
                    ["MaxFrameSize"] = "1500",
                    ["MessageRepeatCount"] = "2",
                    ["MessageRepeatDelay"] = "00:00:00.020",
                    ["PreferredNetworkInterface"] = "192.168.5.10"
                })
                .Build();

            var options = new UdpTransportOptions();
            configuration.Bind(options);

            Assert.That(options.SendBufferSize, Is.EqualTo(8192));
            Assert.That(options.ReceiveBufferSize, Is.EqualTo(16384));
            Assert.That(options.ReceiveQueueCapacity, Is.EqualTo(8));
            Assert.That(options.Ttl, Is.EqualTo(5));
            Assert.That(options.MulticastLoopback, Is.True);
            Assert.That(options.MaxFrameSize, Is.EqualTo(1500));
            Assert.That(options.MessageRepeatCount, Is.EqualTo(2));
            Assert.That(options.MessageRepeatDelay, Is.EqualTo(TimeSpan.FromMilliseconds(20)));
            Assert.That(options.PreferredNetworkInterface, Is.EqualTo("192.168.5.10"));
        }

        [Test]
        public void IConfiguration_Binding_EmptyConfigurationLeavesDefaults()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection([])
                .Build();

            var options = new UdpTransportOptions();
            configuration.Bind(options);

            Assert.That(options.SendBufferSize, Is.EqualTo(64 * 1024));
            Assert.That(options.MaxFrameSize, Is.EqualTo(65507));
            Assert.That(options.MessageRepeatDelay, Is.EqualTo(TimeSpan.FromMilliseconds(5)));
            Assert.That(options.PreferredNetworkInterface, Is.Null);
        }
    }
}
